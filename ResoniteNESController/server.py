import asyncio
import websockets
import pyvjoy

# Button mappings
BUTTON_MAP = {
    "a": 1,
    "b": 2,
    "x": 3,
    "y": 4,
    "u": 5,
    "d": 6,
    "l": 7,
    "r": 8,
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
            btn_name = msg[0]
            action = int(msg[1])  # Convert string to int

            if btn_name in BUTTON_MAP:
                vj.set_button(BUTTON_MAP[btn_name], action)
                print(f"Button {btn_name} {'pressed' if action else 'released'}")
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
