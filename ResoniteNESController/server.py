# server.py

import asyncio
import websockets
import pyvjoy

# Button mappings
BUTTON_MAP = {
    "btn1": 1,
    "btn2": 2,
    "btn3": 3,
    "btn4": 4,
    "up": 5,
    "down": 6,
    "left": 7,
    "right": 8,
}

# Initialize the vJoy device
vj = pyvjoy.VJoyDevice(1)


async def server_handler(websocket, path):
    addr = f"{websocket.remote_address[0]}:{websocket.remote_address[1]}"
    print(f"Client connected from {addr}")

    try:
        async for msg in websocket:
            print(f"Received message '{msg}' from {addr}")

            # Extract button name and action
            btn_name, action = msg.split("_")

            # Check if the button name exists in our map
            if btn_name in BUTTON_MAP:
                if action == "pressed":
                    vj.set_button(BUTTON_MAP[btn_name], 1)
                elif action == "released":
                    vj.set_button(BUTTON_MAP[btn_name], 0)
                print(f"Button {btn_name} {action}")
            else:
                print(f"Unknown button {btn_name}")

            await websocket.send(f"Received: {msg}")

    except websockets.exceptions.ConnectionClosed:
        print(f"Client {addr} disconnected")


server_address = '127.0.0.1'  # localhost for simplicity, change as needed
server_port = 1985

start_server = websockets.serve(server_handler, server_address, server_port, ping_interval=None, ping_timeout=None)

print(f"Server is running at ws://{server_address}:{server_port}")

asyncio.get_event_loop().run_until_complete(start_server)
asyncio.get_event_loop().run_forever()
