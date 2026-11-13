
extends CharacterBody3D

const CAMERA_MARGIN = 0.3

@onready var shoulder_ray = $CameraPivot/ShoulderRay

const FIREBALL = preload("res://spells/fireball.tscn")

@onready var cast_point = $Model/CastPoint
@onready var aim_ray = $CameraPivot/SpringArm3D/Camera3D/AimRay

const TURN_SPEED = 10.0
const AIM_SPEED_MULT = 0.6
const CAMERA_DISTANCE = 3.0
const AIM_CAMERA_DISTANCE = 1.8
const ZOOM_SPEED = 10.0

var is_aiming = false

@onready var model = $Model

const SPEED = 5.0
const JUMP_VELOCITY = 4.5
const MOUSE_SENSITIVITY = 0.003

@onready var pivot = $CameraPivot
@onready var spring_arm = $CameraPivot/SpringArm3D

func _ready():
	
	shoulder_ray.add_exception(self)
	
	aim_ray.add_exception(self)
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	spring_arm.add_excluded_object(get_rid())

func _unhandled_input(event):
	
	if event.is_action_pressed("cast"):
		cast()
	
	if event.is_action_pressed("switch_shoulder"):
		shoulder_side *= -1

	if event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		pivot.rotation.y -= event.relative.x * MOUSE_SENSITIVITY
		pivot.rotation.x -= event.relative.y * MOUSE_SENSITIVITY
		pivot.rotation.x = clamp(pivot.rotation.x, deg_to_rad(-70), deg_to_rad(60))

	if event.is_action_pressed("ui_cancel"):
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE

func _physics_process(delta):
	is_aiming = Input.is_action_pressed("aim")
	if not is_on_floor():
		velocity += get_gravity() * delta

	if Input.is_action_just_pressed("jump") and is_on_floor():
		velocity.y = JUMP_VELOCITY

	var input_dir = Input.get_vector("move_left", "move_right", "move_forward", "move_back")
	var direction = Vector3(input_dir.x, 0, input_dir.y).rotated(Vector3.UP, pivot.rotation.y)
	var speed = SPEED * AIM_SPEED_MULT if is_aiming else SPEED
	velocity.x = direction.x * speed
	velocity.z = direction.z * speed

	if is_aiming:
		model.rotation.y = lerp_angle(model.rotation.y, pivot.rotation.y, TURN_SPEED * delta)
	elif direction.length() > 0:
		var target_yaw = atan2(-direction.x, -direction.z)
		model.rotation.y = lerp_angle(model.rotation.y, target_yaw, TURN_SPEED * delta)

	move_and_slide()
	
const SHOULDER_OFFSET = 0.6
const SHOULDER_SWITCH_SPEED = 10.0

var shoulder_side = 1.0

func _process(delta):
	
	var target_x = SHOULDER_OFFSET * shoulder_side

	shoulder_ray.target_position = Vector3(target_x, 0, 0)
	shoulder_ray.force_raycast_update()
	var max_offset = SHOULDER_OFFSET
	if shoulder_ray.is_colliding():
		var hit_distance = shoulder_ray.global_position.distance_to(shoulder_ray.get_collision_point())
		max_offset = max(hit_distance - CAMERA_MARGIN, 0.0)

	spring_arm.position.x = lerp(spring_arm.position.x, target_x, SHOULDER_SWITCH_SPEED * delta)
	if shoulder_side > 0:
		spring_arm.position.x = min(spring_arm.position.x, max_offset)
	else:
		spring_arm.position.x = max(spring_arm.position.x, -max_offset)
		
	var target_distance = AIM_CAMERA_DISTANCE if is_aiming else CAMERA_DISTANCE
	spring_arm.spring_length = lerp(spring_arm.spring_length, target_distance, ZOOM_SPEED * delta)
	
func cast():
	model.rotation.y = pivot.rotation.y

	aim_ray.force_raycast_update()
	var target = aim_ray.get_collision_point() if aim_ray.is_colliding() else aim_ray.to_global(aim_ray.target_position)

	var fireball = FIREBALL.instantiate()
	get_tree().current_scene.add_child(fireball)
	fireball.global_position = cast_point.global_position
	fireball.direction = (target - fireball.global_position).normalized()
	fireball.caster = self
