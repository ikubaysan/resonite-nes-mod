import socketserver
import json
import pyvjoy
from websocket_server import WebsocketServer

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

def reset_buttons():
    """Set all buttons to unpressed state."""
    for button in BUTTON_MAP.values():
        vj.set_button(button, 0)

def on_client_connected(client, server):
    ip, port = client['address']
    print(f"Client({client['id']}) connected from IP: {ip}")
    reset_buttons()  # Reset buttons when a new client connects
    print(f"Buttons reset after new client connection")


def on_client_disconnected(client, server):
    """Called when a client disconnects."""
    print(f"Client({client['id']}) disconnected")
    reset_buttons()  # Reset buttons when a client disconnects
    print(f"Buttons reset after client disconnection")


def on_message(client, server, message):
    print(f"Received message '{message}' from Client({client['id']})")

    btn_name = message[0]
    try:
        action = int(message[1])  # Convert string to int
    except ValueError:
        print(f"Invalid message '{message}'")
        return

    if btn_name in BUTTON_MAP:
        if action == 0:
            if btn_name in ["u", "d"]:
                vj.set_button(BUTTON_MAP["u"], 0)
                vj.set_button(BUTTON_MAP["d"], 0)
            elif btn_name in ["l", "r"]:
                vj.set_button(BUTTON_MAP["l"], 0)
                vj.set_button(BUTTON_MAP["r"], 0)
            else:
                vj.set_button(BUTTON_MAP[btn_name], 0)
        else:
            if btn_name == "u":
                vj.set_button(BUTTON_MAP["d"], 0)
            elif btn_name == "d":
                vj.set_button(BUTTON_MAP["u"], 0)
            elif btn_name == "l":
                vj.set_button(BUTTON_MAP["r"], 0)
            elif btn_name == "r":
                vj.set_button(BUTTON_MAP["l"], 0)
            vj.set_button(BUTTON_MAP[btn_name], action)
        print(f"Button {btn_name} {'pressed' if action else 'released'}")
    else:
        print(f"Unknown button {btn_name}")

    server.send_message(client, f"Received: {message}")

server_address = '127.0.0.1'  # localhost for simplicity, change as needed
server_port = 1985

server = WebsocketServer(host=server_address, port=server_port)
server.set_fn_new_client(on_client_connected)
server.set_fn_client_left(on_client_disconnected)
server.set_fn_message_received(on_message)
print(f"Server is running at ws://{server_address}:{server_port}")
server.run_forever()
