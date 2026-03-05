# Tomb Raider: The Angel of Darkness Savegame Editor
This is a prototype savegame editor for the classic Tomb Raider: The Angel of Darkness game. For a savegame editor that covers the Tomb Raider I-VI Remastered series,
check out [TRR-SaveMaster](https://github.com/JulianOzelRose/TRR-SaveMaster). This editor is mainly a proof of concept related to reverse engineering and is not heavily tested.
Features include editing items, weapons, ammo, and health.

<img width="699" height="576" alt="TR-AoD-SaveEdit-UI" src="https://github.com/user-attachments/assets/c945e0a6-66e0-4312-b101-4bfe89953a8c" />


# Installation and use
To use this editor, navigate to the [Relase](https://github.com/JulianOzelRose/TR-AoD-SaveEdit/tree/master/TR-AoD-SaveEdit/bin/x64/Release) folder, download the EXE and run it.
No installation is necessary. Once the editor is open, click Browse, then navigate to the root directory of your game. If you are using the Steam version of the game, it should be located at:

`C:\Program Files (x86)\Steam\steamapps\common\Tomb Raider (VI) The Angel of Darkness\`

Once your directory is selected, the editor should automatically populate the savegame list. You can then modify them however you'd like. Before modifying a savegame, the editor
will automatically create backups by duplicating the savegame file and appending '.bak' to the filename. You can use this to revert if a savegame gets bricked.
