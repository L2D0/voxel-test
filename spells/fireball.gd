extends Area3D

const SPEED = 25.0
const LIFETIME = 3.0

var direction = Vector3.FORWARD
var caster = null
var age = 0.0

func _ready():
	body_entered.connect(_on_body_entered)

func _physics_process(delta):
	global_position += direction * SPEED * delta
	age += delta
	if age > LIFETIME:
		queue_free()

func _on_body_entered(body):
	if body == caster:
		return
	queue_free()
