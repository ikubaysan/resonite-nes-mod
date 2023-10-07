# resonite-nes-mod

Audio - For now you can spawn an audio player in Resonite, targeting your desktop audio, and mute all apps aside from the emulator. This would mean audio on desktop would not be supported unless you have multiple audio devices. This is a sort of workaround way for now.



ResoniteNESApp will look for a target window, and capture the RGB values of each of its pixels.
Below is how we represent 2 pixels and which RGB values should be applied to them.

'''
	[
	row index, column index, r value, g value, b value,
	row index, column index, r value, g value, b value,
	...
	]
	
'''

There are much more optimal ways of representing this, but keeping things simple for now.








## Creating a Canvas
Since we're patching the FrooxEngine.Animator.OnCommonUpdate() method, there must be an Animator component somewhere in the world for this method to hit. 
Therefore, it's best to just attach an empty Animator component to the root of the canvas.




## How to run
* Start FCEUX
* Run controller python script
* Run ResoniteNESApp
* Load Resonite with mod
* Spawn a NESUIXCanvas