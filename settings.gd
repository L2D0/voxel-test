extends Node

signal draw_distance_changed(value)

var draw_distance = 4:
	set(value):
		draw_distance = value
		draw_distance_changed.emit(value)
