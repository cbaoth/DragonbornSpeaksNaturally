# How to Use Key Commands
Edit your DragonbornSpeaksNaturally.ini file and under the [ConsoleCommands] heading, add your commands like this:
```
[ConsoleCommands]
Open Map=press m
Close Map=press m
What should I do=press j
I don't want any more=press esc
Hello, man=press e
Hello, brother=press e
I want this=press e
Sneak and cancel=switchwindow; press ctrl; sleep 5000; press ctrl
```

# Available Commands

## Command press
```
press <key name or DirectInput scan code> [millisecond] ...
```

### Description
Simulate pressing the specified key the specified milliseconds.
Used to cast skills or dragon shouts or do other actions.
If time is omitted, the time will be set as 50 milliseconds.

### Tips
1. If you want to use scan code 0-9, please add the prefix 0x.
A single digit without 0x prefix will be considered a digit key instead of a key code.
Exampke: `0x01` is esc, but `1` is the number key 1.
2. Only the currently active window can receive the simulated key.
Activate the Skyrim window by calling switchwindow command if necessary.
3. Only one key can be represented when the time is omitted.
To tap more keys without time, use the tapkey command.
4. Mouse event is supported. Use the following key names:
    ```
    leftmousebutton  rightmousebutton  middlemousebutton
    mousewheelup     mousewheeldown
    mousebutton4     mousebutton5
    ```
    However, the cursor must be inside the Skyrim window when simulating a mouse click,
    otherwise the other window will receive the click event instead of Skyrim.


### Reference
* [Windows DirectInput scan codes](https://www.creationkit.com/index.php?title=Input_Script#DXScanCodes)
* [Avaliable key names](https://github.com/YihaoPeng/DragonbornSpeaksNaturally/blob/master/dsn_plugin/dsn_plugin/KeyCode.hpp)

### Example
```
press m
press Z 1000
press 44 50
press 0x2C 100
press esc

; Press 3 keys at the same time (ctrl + alt + a):
press  ctrl 500  alt 500  a 400

; left hand magic
press  leftmousebutton 1000
````

## Command tapkey
```
tapkey <key name or DirectInput scan code> ...
```

### Description:
It's a shortcut to the press command (All pressing time are set to 50 milliseconds).

### Example
```
; Press 3 keys at the same time (ctrl + alt + a):
tapkey ctrl alt a
```

### Command holdkey and releasekey
```
holdkey <key name or DirectInput scan code> ...
releasekey <key name or DirectInput scan code> ...
```

### Description:
Manual control the press and release of keys.

### Example:
```
; typing with shift hold:
tapkey ~; sleep 100; holdkey shift; tapkey t e; tapkey s t; releasekey shift; sleep 100; tapkey t e; tapkey s t

; casting magic with double hands
holdkey leftmousebutton; sleep 1000; holdkey rightmousebutton; sleep 5000; releasekey leftmousebutton; sleep 3000; releasekey rightmousebutton
```

## Command sleep
```
sleep <millisecond>
```

### Description:
Delays the subsequent commands the specified milliseconds.
Used to form an automatic action script with the press command.
Both custom commands and Skyrim commands can be delayed.

### Example:
```
sleep 5000
press z; sleep 1000; press LeftMouseButton 2000
press ctrl; sleep 5000; press ctrl

; Casting two dragon shouts one after another:
player.cast 0003f9ed player voice; sleep 3000; player.cast 00013f3a player voice
```


## Command switchwindow
```
switchwindow [window title|executable name]
```

### Description:
Activate the specified window.
Used to switch to the correct window before running the press command.
Omitting the parameters will activate the current Skyrim window.

### TODO:
Move the mouse cursor to the center of the active window.

### Example:
```
switchwindow
switchwindow Notepad++
switchwindow notepad.exe

; Activate the Skyrim window and type in the console:
switchwindow; sleep 50; tapkey ~; sleep 50; tapkey s a v e enter; sleep 50; tapkey ~
```
