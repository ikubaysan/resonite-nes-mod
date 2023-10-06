import asyncio
import websockets
import keyboard

# Define the keys for the buttons and their corresponding button names
KEY_MAP = {
    "up arrow": "u",
    "down arrow": "d",
    "left arrow": "l",
    "right arrow": "r",
    "z": "a",
    "x": "b",
    "c": "x",
    "v": "y"
}

button_states = {key: False for key in KEY_MAP.keys()}

async def send_input():
    while True:
        try:
            async with websockets.connect('ws://127.0.0.1:1985') as websocket:
                print("Connected to server.")
                while True:
                    # Send heartbeat
                    try:
                        await websocket.ping()
                        await asyncio.wait_for(websocket.pong(), timeout=10)  # Wait for pong response
                    except:
                        print("No Pong received. Disconnecting...")
                        raise websockets.exceptions.ConnectionClosedError

                    for key, btn_name in KEY_MAP.items():
                        if keyboard.is_pressed(key) and not button_states[key]:
                            await websocket.send(f"{btn_name}1")
                            print(f"Sent message '{btn_name}1' to server")
                            button_states[key] = True
                        elif not keyboard.is_pressed(key) and button_states[key]:
                            await websocket.send(f"{btn_name}0")
                            print(f"Sent message '{btn_name}0' to server")
                            button_states[key] = False

                    await asyncio.sleep(0.1)

        except (websockets.exceptions.ConnectionClosedError, ConnectionRefusedError):
            print("Connection lost. Retrying...")
            await asyncio.sleep(1)


asyncio.get_event_loop().run_until_complete(send_input())
