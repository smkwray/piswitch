; PiSwitch — AutoHotkey trigger script
; Binds Ctrl+Space to show the PiSwitch pie menu.
; Edit the hotkey below to your preference.
;
; Requirements:
;   - PiSwitch.exe must be running in the background (launch it once first)
;   - AutoHotkey v2 (https://www.autohotkey.com/)
;
; Common hotkey modifiers:
;   ^ = Ctrl, ! = Alt, + = Shift, # = Win
;   Examples: ^Space (Ctrl+Space), !Space (Alt+Space), ^!p (Ctrl+Alt+P)

#Requires AutoHotkey v2.0

; Instance name — change if using multiple instances
INSTANCE := "default"

; Ctrl+Space to show PiSwitch
^Space:: {
    SignalPiSwitch(INSTANCE)
}

SignalPiSwitch(instance := "default") {
    ; Open the named event that PiSwitch daemon listens on
    static EVENT_MODIFY_STATE := 0x0002
    eventName := "Local\PiSwitch_show_" instance

    hEvent := DllCall("OpenEvent", "UInt", EVENT_MODIFY_STATE, "Int", 0, "Str", eventName, "Ptr")
    if (hEvent) {
        DllCall("SetEvent", "Ptr", hEvent)
        DllCall("CloseHandle", "Ptr", hEvent)
    }
}
