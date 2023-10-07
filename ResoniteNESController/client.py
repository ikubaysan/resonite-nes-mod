import websocket
import keyboard
import time

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

def on_message(ws, message):
    print(message)

def on_error(ws, error):
    print(f"Error: {error}")

def on_close(ws, close_status_code, close_msg):
    print("### closed ###")

def on_open(ws):
    print("Connected to server.")
    while True:
        for key, btn_name in KEY_MAP.items():
            if keyboard.is_pressed(key) and not button_states[key]:
                ws.send(f"{btn_name}1")
                print(f"Sent message '{btn_name}1' to server")
                button_states[key] = True
            elif not keyboard.is_pressed(key) and button_states[key]:
                ws.send(f"{btn_name}0")
                print(f"Sent message '{btn_name}0' to server")
                button_states[key] = False
        time.sleep(0.1)

if __name__ == "__main__":
    websocket.enableTrace(False)
    ws = websocket.WebSocketApp("ws://127.0.0.1:1985",
                                on_message=on_message,
                                on_error=on_error,
                                on_close=on_close
                                )
    ws.on_open = on_open
    ws.run_forever()
