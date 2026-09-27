# App volume setup (v1.0)

Select up to five apps in the Companion tray → App Volume Controls. Play audio
in an app and click Refresh apps if it is missing. Read the Slot column, then
open the SmartThings device and use the pencil beside the matching App 1–5
heading to name it manually. There is no separate App Name card.

PC의 앱별 볼륨 제어에서 슬롯 번호를 확인한 뒤 SmartThings의 해당 App 옆
연필 버튼으로 앱 이름을 직접 입력하세요. 다른 앱을 할당하면 이름도 변경하세요.

Selections use the first free slot and persist across restarts. Removing an app
does not move the others. Use the displayed slot number rather than list order.
Each slot contains only standard audioVolume/audioMute capabilities, retaining
Routines support. Main playback and master-volume controls are unchanged.
Empty or inactive slots do not send commands to the master output.

The Companion list refreshes only when opened or when Refresh apps is clicked.
Existing app child devices are retired on migration; update any old child
Routine references. The parent device does not need to be deleted or paired again.

See [protocol](protocol.md#selected-app-volume-channels) for slot persistence,
stale-command checks and audio-session identity. Automated tests cover these
rules, component routing and state clearing. Phone testing confirmed native
volume/mute controls. Dynamic title and grouped-name presentation experiments
did not render correctly on the tested Android app, so v1.0 uses manual headings.
