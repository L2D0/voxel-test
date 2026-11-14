extends CanvasLayer

func _ready():
	visible = false
	%ResumeButton.pressed.connect(close)
	%QuitButton.pressed.connect(get_tree().quit)
	%DrawDistanceSlider.value = Settings.draw_distance
	%DrawDistanceSlider.value_changed.connect(_on_draw_distance_changed)
	_update_draw_distance_label()

func _unhandled_input(event):
	if event.is_action_pressed("ui_cancel"):
		if visible:
			close()
		else:
			open()
		get_viewport().set_input_as_handled()

func _notification(what):
	if what == NOTIFICATION_APPLICATION_FOCUS_OUT and not visible:
		open()

func open():
	visible = true
	get_tree().paused = true
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE

func close():
	visible = false
	get_tree().paused = false
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED

func _on_draw_distance_changed(value):
	Settings.draw_distance = int(value)
	_update_draw_distance_label()

func _update_draw_distance_label():
	%DrawDistanceLabel.text = "Draw distance: %d chunks" % Settings.draw_distance
