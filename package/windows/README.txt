Yakuza PC Port V2.0.0 | Windows x64 | iso-gz branch
Fork of Ivan Barbosa Filho's Yakuza PC Port

1. Extract the entire Yakuza-win64 folder to a writable location.
2. Open Yakuza.exe and choose your .iso or .iso.gz. A prepatched Yakuza
   Restored image can be selected directly. Gzip imports need room for
   one expanded ISO under Discs. The port remembers the image on later runs.
3. The game starts in a borderless desktop window. Press Alt+Enter to toggle
   to a maximized, resizable window and back. Borderless uses the current
   monitor with a one-pixel bottom margin. The port's original F11 fullscreen
   mode can show a purple screen on some systems.

To start in the large, framed window every time, set window_layout=large
in Yakuza.ini. To choose a different image, drop it on Yakuza.exe or use
Yakuza.exe --choose. The original --iso and --patch options also work.
If the game already detects your Restored image, no extra patch is needed.

The gzip reader accepts any gzip-compressed image. The original runtime
checks whether it can run the disc. Settings, memory card and logs use
the port's normal locations. For a clean install, you can copy a previous
memory card named memcard0.ps2 into this folder with the game closed.

Upstream: https://github.com/IvanBarbosaFilho/Yakuza-Pc-Port
Fork: https://github.com/TatsunaKyo/Yakuza-Pc-Port/tree/iso-gz
