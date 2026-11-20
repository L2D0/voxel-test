using Godot;
using System.Collections.Generic;

public enum AwareState { Idle, Notice, Chase, Search, Sweep, Return, Flee }

// The enemy's brain: what it sees and hears, who it's angry at, and what it does about it.
// Host only. It never moves the body itself; it tells the body where to go and what to look at.
//
//   Idle    roams, alone or following its pack leader; anchored enemies wander near home instead
//   Notice  saw or heard something: stops, stares, fills its alert meter
//   Chase   hunts the top of its threat list
//   Search  walks to where it last saw or heard something
//   Sweep   wanders around that spot for a while, looking about
//   Return  anchored only: gives up and walks home, blind and deaf until it gets there.
//           Roamers just go back to Idle where they stand, and calm down for a few seconds after a leash.
//   Flee    runs from something it's scared of, or from anything once it's badly hurt
public partial class Enemy
{
    // How often it looks around. Level of detail: the further the nearest player, the less often it thinks.
    // Sight tops out around 20 m, so beyond the near band a slower brain loses nothing anyone can see.
    private const float SenseInterval = 0.2f;
    private const float MidSenseInterval = 0.5f, MidDistance = 30f;
    private const float FarSenseInterval = 1f, FarDistance = 60f;
    private float EyeHeight => (Def?.BodyHeight ?? 2f) * 0.3f;   // grunts keep 0.6; small things see from lower down
    private const float SwitchMargin = 1.25f;   // a new target needs this much more threat to steal its attention
    private const uint SightBlockers = SpellFx.WorldLayer | SpellFx.StructureLayer;   // terrain and extrusions, not bodies

    private readonly RandomNumberGenerator _rng = new();
    private readonly Dictionary<Node3D, float> _threat = new();
    private readonly List<Node3D> _scratch = new();

    private AwareState _state = AwareState.Idle;
    private AwareState _shownState = AwareState.Idle;   // what the label shows; synced to every machine
    private float _stateTime;
    private float _senseTimer;
    private float _nearestPlayer = float.MaxValue;   // refreshed on every look around

    private Node3D _target;        // what it's chasing
    private bool _targetSeen;      // seen on the last look around
    private Vector3 _lastSeen;     // where the target was last seen
    private float _alert;          // 0 to 1; reaching 1 starts a chase
    private Node3D _suspect;       // what it's staring at, if it can see it
    private Vector3 _interest;     // the spot it's staring at or searching
    private float _noticeLost;
    private Node3D _fleeFrom;
    private float _fleeLost;
    private bool _leashed;         // anchored: gave up after going too far; ignores everything until home
    private float _calmTimer;      // roamers: gave up after going too far; ignores everything until this runs out
    private Vector3 _chaseOrigin;  // where the chase began; the leash measures from here
    private List<Enemy> _pack;     // everyone it spawned with, if it's a pack animal; host only

    private bool Calm => _leashed || _calmTimer > 0f;

    private Vector3 _wanderPoint;
    private float _pauseTimer;
    private float _legTimer;

    // ---------- The loop ----------

    private void Think(float dt)
    {
        _stateTime += dt;
        _calmTimer -= dt;
        DecayThreat(dt);
        TickAbilities(dt);

        _senseTimer -= dt;
        if (_senseTimer <= 0f)
        {
            float interval = CurrentSenseInterval();
            _senseTimer += interval;
            Sense(interval);
        }

        switch (_state)
        {
            case AwareState.Idle: TickIdle(dt); break;
            case AwareState.Notice: TickNotice(dt); break;
            case AwareState.Chase: TickChase(); break;
            case AwareState.Search: TickSearch(); break;
            case AwareState.Sweep: TickSweep(dt); break;
            case AwareState.Return: TickReturn(); break;
            case AwareState.Flee: TickFlee(dt); break;
        }
    }

    private void SetState(AwareState state)
    {
        if (state != AwareState.Chase) CancelWindup();   // fleeing or losing the target ends the attack
        _state = state;
        _stateTime = 0f;   // re-entering a state (a new search spot) restarts its clock

        if (state == AwareState.Idle) _pauseTimer = _rng.RandfRange(0.5f, 2f);
        if (state == AwareState.Notice) _noticeLost = 0f;
        if (state == AwareState.Sweep) _pauseTimer = 0.5f;

        if (state != _shownState) Rpc(MethodName.ShowState, (int)state);
    }

    // Debug readout for now; later this drives "?" and "!" over heads and the creature's barks
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowState(int state)
    {
        _shownState = (AwareState)state;
        UpdateLabel();
    }

    // ---------- Senses ----------

    private float CurrentSenseInterval() =>
        _nearestPlayer > FarDistance ? FarSenseInterval
        : _nearestPlayer > MidDistance ? MidSenseInterval
        : SenseInterval;

    private void Sense(float dt)
    {
        _nearestPlayer = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup("players"))
            if (node is Node3D player) _nearestPlayer = Mathf.Min(_nearestPlayer, GlobalPosition.DistanceTo(player.GlobalPosition));

        if (Calm) return;
        _targetSeen = false;

        Node3D hostile = null, scary = null;
        float hostileDist = float.MaxValue, scaryDist = float.MaxValue;

        foreach (Node3D other in Candidates())
        {
            Response response = ResponseTo(other);
            if (response == Response.Ignore) continue;
            if (response == Response.Defend && !_threat.ContainsKey(other)) continue;   // hasn't started anything yet
            if (!CanSee(other, out float dist)) continue;

            if (other == _target)
            {
                _targetSeen = true;
                _lastSeen = other.GlobalPosition;
            }
            if (other == _fleeFrom) _fleeLost = 0f;

            if (response == Response.Flee || (response == Response.Avoid && dist < Def.AvoidDistance))
            {
                if (dist < scaryDist) { scary = other; scaryDist = dist; }
                continue;
            }
            if (response == Response.Avoid) continue;

            // Attack, or Defend against something that already hit us
            if (_state == AwareState.Chase)
            {
                AddThreat(other, dt * Closeness(dist));   // anyone hostile in view stays on the list
                continue;
            }
            if (dist < hostileDist) { hostile = other; hostileDist = dist; }
        }

        if (scary != null && _state != AwareState.Flee)
        {
            StartFlee(scary);
            return;
        }
        if (_state is AwareState.Chase or AwareState.Flee) return;

        if (hostile == null)
        {
            _alert = Mathf.Max(_alert - dt * 0.5f, 0f);
            _suspect = null;
            return;
        }

        // The alert meter fills faster up close, and faster still if it's already on edge
        float rate = dt / Mathf.Max(Def.NoticeTime, 0.05f) * Closeness(hostileDist);
        if (_state is AwareState.Search or AwareState.Sweep) rate *= 2f;
        if (hostileDist <= Def.CloseSense) rate = 1f;

        _alert += rate;
        _suspect = hostile;
        _interest = hostile.GlobalPosition;

        if (_alert >= 1f)
        {
            StartChase(hostile);
            CallPack(hostile);
        }
        else if (_state is AwareState.Idle or AwareState.Return)
        {
            SetState(AwareState.Notice);
        }
    }

    // 2 at point blank down to 0.5 at the edge of sight
    private float Closeness(float dist) => Mathf.Lerp(2f, 0.5f, Mathf.Clamp(dist / Def.SightRange, 0f, 1f));

    private bool CanSee(Node3D other, out float dist)
    {
        Vector3 to = other.GlobalPosition - GlobalPosition;
        dist = to.Length();
        float visibility = other is PlayerController player ? player.Visibility : 1f;   // crouching now, stealth nodes later
        if (dist > Def.SightRange * visibility) return false;

        // Outside close range it only sees inside its cone
        if (dist > Def.CloseSense * visibility && Def.SightAngle < 360f)
        {
            Vector3 flat = Flat(to);
            Vector3 forward = Flat(-GlobalBasis.Z).Normalized();
            if (flat.LengthSquared() > 0.01f &&
                forward.Dot(flat.Normalized()) < Mathf.Cos(Mathf.DegToRad(Def.SightAngle * 0.5f)))
                return false;
        }

        // Low and high, so a half-covered player still counts as seen
        Vector3 eye = GlobalPosition + Vector3.Up * EyeHeight;
        return ClearLine(eye, other.GlobalPosition + Vector3.Up * 0.4f)
            || ClearLine(eye, other.GlobalPosition + Vector3.Up * 1.2f);
    }

    private bool ClearLine(Vector3 from, Vector3 to)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to, SightBlockers);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        return GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
    }

    // Host only, called by Noise.Emit
    public void Hear(Vector3 at, float radius, Node3D source)
    {
        if (_health.IsDead || Calm || Def.HearingScale <= 0f) return;

        float range = radius * Def.HearingScale;
        float dist = GlobalPosition.DistanceTo(at);
        if (dist > range) return;

        if (source != null && IsAlive(source))
        {
            Response response = ResponseTo(source);
            if (response == Response.Ignore) return;
            if (response is Response.Flee or Response.Avoid)
            {
                if (dist < range * 0.5f && _state != AwareState.Flee) StartFlee(source);
                return;
            }
        }

        if (_state is AwareState.Chase or AwareState.Flee) return;   // busy

        if (dist < range * 0.4f || _state is AwareState.Search or AwareState.Sweep)
        {
            _alert = Mathf.Max(_alert, 0.5f);
            StartSearch(at);   // close and loud, or already hunting: go straight there
        }
        else if (_state is AwareState.Idle or AwareState.Return)
        {
            _interest = at;
            _suspect = null;
            SetState(AwareState.Notice);
        }
    }

    // ---------- Who's who ----------

    private IEnumerable<Node3D> Candidates()
    {
        foreach (Node node in GetTree().GetNodesInGroup("players"))
            if (node is Node3D player && IsAlive(player)) yield return player;
        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
            if (node is Node3D enemy && enemy != this && IsAlive(enemy)) yield return enemy;
    }

    // The first line of the table whose tag matches wins. Its own kind is always left alone.
    private Response ResponseTo(Node3D other)
    {
        if (other is Enemy enemy && enemy.Def == Def) return Response.Ignore;

        foreach (TagResponse line in Def.Responses)
            if (line != null && HasTag(other, line.Tag)) return line.Response;
        return Response.Ignore;
    }

    // Players and their summons all count as "Player"; enemies carry their definition's tags
    private static bool HasTag(Node3D other, string tag)
    {
        if (tag == "Player") return Teams.IsPlayerSide(other);
        return other is Enemy enemy && enemy.Def.Tags.Contains(tag);
    }

    private static bool IsAlive(Node3D node) =>
        IsInstanceValid(node) && node.IsInsideTree() && Health.Of(node) is Health health && !health.IsDead;

    // ---------- Threat ----------

    private void AddThreat(Node3D who, float amount)
    {
        if (who == null || who == this) return;
        _threat[who] = (_threat.TryGetValue(who, out float current) ? current : 0f) + amount;
    }

    private void DecayThreat(float dt)
    {
        if (_threat.Count == 0) return;
        float keep = Mathf.Exp(-dt / Mathf.Max(Def.ThreatMemory, 0.1f));

        _scratch.Clear();
        _scratch.AddRange(_threat.Keys);
        foreach (Node3D who in _scratch)
        {
            if (!IsAlive(who)) _threat.Remove(who);   // dead or gone: forget them
            else _threat[who] *= keep;
        }
    }

    private Node3D TopThreat()
    {
        Node3D best = null;
        float bestValue = 0f;
        foreach (var (who, value) in _threat)
            if (value > bestValue) { best = who; bestValue = value; }

        // Don't flip between two targets with nearly the same threat
        if (_target != null && best != _target && _threat.TryGetValue(_target, out float current)
            && bestValue < current * SwitchMargin)
            return _target;
        return best;
    }

    // ---------- Reactions ----------

    private void ReactToDamage(float amount, Node3D source)
    {
        if (_health.IsDead || source == null || source == this || !IsAlive(source)) return;
        if (source is Enemy enemy && enemy.Def == Def) return;   // its own kind's splash later: shrug it off

        AddThreat(source, amount);
        _lastSeen = source.GlobalPosition;   // it knows roughly where that came from

        Response response = ResponseTo(source);
        if (response is Response.Flee or Response.Avoid || ShouldFleeHurt())
        {
            StartFlee(source);
            return;
        }
        if (Calm) return;   // you can't pull it back by poking it right after it gave up

        // Anything it ignored becomes fair game once it hits first
        if (_state != AwareState.Chase) StartChase(source);
        CallPack(source);
    }

    // Its own pack always joins in; same kind nearby does too. Only one hop, so a fight can't drag the whole map in.
    private void CallPack(Node3D target)
    {
        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
            if (node is Enemy other && other != this && other.Def == Def
                && ((_pack != null && _pack.Contains(other))
                    || other.GlobalPosition.DistanceTo(GlobalPosition) <= Def.PackCallRadius))
                other.Alert(target, _lastSeen);
    }

    // ---------- Packs ----------

    // Host only; the spawner calls it on a group it just spawned
    public static void FormPack(List<Enemy> members)
    {
        foreach (Enemy member in members) member._pack = members;
    }

    // The first one still alive leads. When it dies, the next one takes over.
    private Enemy PackLeader()
    {
        if (_pack == null) return null;
        foreach (Enemy member in _pack)
            if (IsAlive(member)) return member;
        return null;
    }

    private void Alert(Node3D target, Vector3 seenAt)
    {
        if (_health.IsDead || Calm || !IsAlive(target)) return;

        if (ResponseTo(target) is Response.Flee or Response.Avoid)
        {
            if (_state != AwareState.Flee) StartFlee(target);
            return;
        }

        AddThreat(target, 1f);
        if (_state == AwareState.Chase) return;

        StartChase(target);
        _lastSeen = seenAt;
        _targetSeen = false;   // it heard about it; it hasn't seen it yet
    }

    private bool ShouldFleeHurt() => Def.FleeHealth > 0f && _health.Current < _health.Max * Def.FleeHealth;

    // ---------- States ----------

    private void StartChase(Node3D target)
    {
        if (target == null || Calm) return;
        if (!_threat.ContainsKey(target)) AddThreat(target, 1f);
        if (_state != AwareState.Chase) _chaseOrigin = Def.Anchored ? Home : GlobalPosition;

        _target = target;
        _lastSeen = target.GlobalPosition;
        _targetSeen = true;
        _alert = 1f;
        SetState(AwareState.Chase);
    }

    private void TickChase()
    {
        if (ShouldFleeHurt() && _target != null)
        {
            StartFlee(_target);
            return;
        }

        Node3D top = TopThreat();
        if (top == null)
        {
            StartReturn();   // everyone it was after is dead or gone
            return;
        }
        if (top != _target)
        {
            _target = top;
            _lastSeen = top.GlobalPosition;
            _targetSeen = false;
        }

        if (Flat(GlobalPosition - _chaseOrigin).Length() > Def.LeashRadius)
        {
            GiveUp();
            return;
        }

        if (Busy) return;   // winding up or recovering: Enemy.Abilities.cs holds it still

        float distance = GlobalPosition.DistanceTo(_target.GlobalPosition);
        if (TryStartAbility(distance, _targetSeen)) return;

        if (_targetSeen)
        {
            MoveTo(_target.GlobalPosition, Def.ChaseSpeedScale, PreferredRange());
            LookToward(_target.GlobalPosition);
        }
        else
        {
            // Lost sight: run to where it was last seen, then search around
            MoveTo(_lastSeen, Def.ChaseSpeedScale);
            StopLooking();
            if (Arrived(_lastSeen)) StartSweep(_lastSeen);
        }
    }

    private void TickNotice(float dt)
    {
        Stop();
        LookToward(_suspect != null ? _suspect.GlobalPosition : _interest);

        // Lost it (or it was only ever a sound): go and check the spot
        _noticeLost = _suspect != null ? 0f : _noticeLost + dt;
        if (_noticeLost > 1.5f) StartSearch(_interest);
    }

    private void StartSearch(Vector3 at)
    {
        _interest = at;
        SetState(AwareState.Search);
    }

    private void TickSearch()
    {
        StopLooking();
        MoveTo(_interest, 1.2f);
        if (Arrived(_interest) || _stateTime > 20f) StartSweep(_interest);   // can't get there? search from here
    }

    private void StartSweep(Vector3 around)
    {
        _interest = around;
        SetState(AwareState.Sweep);
    }

    private void TickSweep(float dt)
    {
        if (_stateTime > Def.SweepTime)
        {
            StartReturn();
            return;
        }

        if (_pauseTimer > 0f)
        {
            // Stand and look around
            _pauseTimer -= dt;
            Stop();
            if (_pauseTimer <= 0f) PickWanderPoint(_interest, Def.SweepRadius);
            return;
        }

        StopLooking();
        MoveTo(_wanderPoint);
        _legTimer += dt;
        if (Arrived(_wanderPoint) || _legTimer > 6f)
        {
            _pauseTimer = _rng.RandfRange(1f, 2.5f);
            float angle = _rng.Randf() * Mathf.Tau;
            LookToward(GlobalPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 5f);
        }
    }

    // Chased too far
    private void GiveUp()
    {
        if (Def.Anchored) _leashed = true;
        else _calmTimer = Def.CalmTime;
        StartReturn();
    }

    // Done with whatever it was doing. Anchored enemies walk home; roamers carry on from where they are.
    private void StartReturn()
    {
        _threat.Clear();
        _target = null;
        _alert = 0f;
        StopLooking();
        SetState(Def.Anchored ? AwareState.Return : AwareState.Idle);
    }

    private void TickReturn()
    {
        MoveTo(Home);
        if (Arrived(Home, 1.5f) || _stateTime > 30f)
        {
            _leashed = false;
            SetState(AwareState.Idle);
        }
    }

    private void TickIdle(float dt)
    {
        StopLooking();
        Enemy leader = PackLeader();
        bool following = leader != null && leader != this;

        // Fell behind the pack: catch up without stopping, then pick a spot near the leader
        if (following && Flat(leader.GlobalPosition - GlobalPosition).Length() > Def.PackSpread * 2f)
        {
            MoveTo(leader.GlobalPosition, 1.3f, Def.PackSpread);
            _pauseTimer = 0.01f;
            return;
        }

        if (_pauseTimer > 0f)
        {
            _pauseTimer -= dt;
            Stop();
            if (_pauseTimer <= 0f) PickIdlePoint(leader, following);
            return;
        }

        MoveTo(_wanderPoint);
        _legTimer += dt;
        if (Arrived(_wanderPoint) || _legTimer > 8f) _pauseTimer = _rng.RandfRange(2f, 5f);
    }

    private void PickIdlePoint(Enemy leader, bool following)
    {
        if (following) PickWanderPoint(leader.GlobalPosition, Def.PackSpread);
        else if (Def.Anchored) PickWanderPoint(Home, Def.WanderRadius);
        else
        {
            // Roam: keep roughly the way it was heading, so it travels instead of pacing in place
            float heading = Rotation.Y + _rng.RandfRange(-1f, 1f);   // about 60 degrees either way
            Vector3 dir = new(-Mathf.Sin(heading), 0f, -Mathf.Cos(heading));
            _wanderPoint = GlobalPosition + dir * Def.WanderRadius * _rng.RandfRange(0.5f, 1f);
            _legTimer = 0f;
        }
    }

    private void PickWanderPoint(Vector3 center, float radius)
    {
        float angle = _rng.Randf() * Mathf.Tau;
        float dist = radius * Mathf.Sqrt(_rng.Randf());   // even spread over the circle, not bunched at the middle
        _wanderPoint = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
        _legTimer = 0f;
    }

    private void StartFlee(Node3D from)
    {
        if (from == null) return;
        _fleeFrom = from;
        _fleeLost = 0f;
        StopLooking();
        SetState(AwareState.Flee);
    }

    private void TickFlee(float dt)
    {
        if (_fleeFrom == null || !IsAlive(_fleeFrom))
        {
            StartReturn();
            return;
        }

        Vector3 away = Flat(GlobalPosition - _fleeFrom.GlobalPosition);
        float dist = away.Length();
        if (dist < 0.1f) away = Vector3.Forward;
        MoveTo(GlobalPosition + away.Normalized() * 5f, Def.ChaseSpeedScale, 0f);

        // Avoid just keeps its distance; Flee runs until it's lost them for a while
        bool avoiding = ResponseTo(_fleeFrom) == Response.Avoid && !ShouldFleeHurt();
        _fleeLost += dt;
        if (avoiding && dist > Def.AvoidDistance + 2f) SetState(AwareState.Idle);
        else if (_fleeLost > 3f && dist > Def.AvoidDistance) StartReturn();
    }
}
