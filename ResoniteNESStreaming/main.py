import pygetwindow as gw
import pyautogui
import cv2
import subprocess
import numpy as np

# Get a specific window by title
window = gw.getWindowsWithTitle('Notepad++')[0]

# Set up FFmpeg command for RTP streaming (this is a simplified example)
cmd = [
    'ffmpeg',
    '-f', 'rawvideo',
    '-pixel_format', 'bgr24',
    '-video_size', f"{window.width}x{window.height}",
    '-i', '-',
    '-an',
    '-c:v', 'libx264',
    '-f', 'rtp',
    'rtp://localhost:1892'
]
process = subprocess.Popen(cmd, stdin=subprocess.PIPE)

while True:
    # Capture the window content
    screenshot = pyautogui.screenshot(region=(window.left, window.top, window.width, window.height))
    frame = cv2.cvtColor(np.array(screenshot), cv2.COLOR_RGB2BGR)

    # Write the frame to FFmpeg for encoding and RTP streaming
    process.stdin.write(frame.tobytes())