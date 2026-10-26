# Steam 磁盘空间痛点 — Reddit 数据分析原始记录

日期：2026-10-26。用于论证 `docs/superpowers/specs/2026-10-26-disk-space-reclaim-features-design.md` 里三个功能（孤儿目录检测、Workshop 残留检测、批量卸载预览）的立项依据，原来只在对话记录里，现在补一份完整存档。

方法：每个痛点用 6 条查询词在 Google（经 Serper API）搜索，统计命中的 Reddit 帖子并去重。

## 孤儿安装目录检测

### 查询词

- `steam leftover game folder after uninstall still exists reddit`
- `steamapps common folder not deleted after uninstall reddit`
- `orphaned steam game files disk space reddit`
- `steam uninstall doesn't remove folder reddit`
- `ghost steam game folder taking space reddit`
- `steam game folder remains after uninstall gigabytes reddit`

### 命中的不重复 Reddit 帖子（36 条）

- https://www.reddit.com/r/EliteDangerous/comments/14eijz8/uninstalled_game_still_takes_up_50gb_in_the_steam
- https://www.reddit.com/r/NobaraProject/comments/1s3d0nx/steam_doesnt_uninstall_games_completely
- https://www.reddit.com/r/Steam/comments/11bxsv9/why_does_steam_take_up_so_much_space_i_dont_have
- https://www.reddit.com/r/Steam/comments/11pkdm8/what_exactly_would_mess_up_if_i_delete_a_game
- https://www.reddit.com/r/Steam/comments/11wegqe/uninstalled_games_still_taking_up_disk_space
- https://www.reddit.com/r/Steam/comments/18425ma/deleted_steam_games_still_taking_up_lots_of_disk
- https://www.reddit.com/r/Steam/comments/18xvz1i/steam_games_folders_still_exist_inside_pc_after
- https://www.reddit.com/r/Steam/comments/19z535/can_i_uninstall_steam_and_keep_my_games_folder
- https://www.reddit.com/r/Steam/comments/1atic0t/uninstalled_steam_game_leaves_behind_over_50gb_of
- https://www.reddit.com/r/Steam/comments/1bp9rb1/uninstalling_steam_games_eats_some_gb_still
- https://www.reddit.com/r/Steam/comments/1bsen7p/wife_uninstalled_steam_before_uninstalling_games
- https://www.reddit.com/r/Steam/comments/1ck4wq3/can_i_delete_steamapps_common_files
- https://www.reddit.com/r/Steam/comments/1d06zwq/will_uninstalling_games_delete_my_progress_if_i
- https://www.reddit.com/r/Steam/comments/1frzpi5/why_does_steam_keep_telling_me_there_isnt_enough
- https://www.reddit.com/r/Steam/comments/1iczelq/can_you_delete_uninstalled_games_from_steamapps
- https://www.reddit.com/r/Steam/comments/1iyctxi/is_this_supposed_to_happen_why_does_steam_take_up
- https://www.reddit.com/r/Steam/comments/1jy71uj/i_uninstalled_a_game_but_i_did_not_get_my_storage
- https://www.reddit.com/r/Steam/comments/1kj5nsj/is_it_fine_for_me_to_delete_the_game_files_inside
- https://www.reddit.com/r/Steam/comments/1kke76u/what_data_is_kept_after_uninstalling_a_game
- https://www.reddit.com/r/Steam/comments/1kxdifi/i_cant_seem_to_find_what_is_taking_up_all_this
- https://www.reddit.com/r/Steam/comments/1ljssyf/i_cant_delete_games_on_my_computer
- https://www.reddit.com/r/Steam/comments/1v152ef/uninstalled_steam_games_still_using_storage
- https://www.reddit.com/r/Steam/comments/6akpvm/is_safe_to_delete_folder_from_the_steamappscommon
- https://www.reddit.com/r/Steam/comments/8ysbiv/can_i_uninstall_steam_just_to_reinstall_but_not
- https://www.reddit.com/r/Steam/comments/9hfwjj/is_it_okay_for_me_to_delete_the_game_files_inside
- https://www.reddit.com/r/Steam/comments/9hmqob/if_i_delete_a_game_will_there_be_any_left_over
- https://www.reddit.com/r/Steam/comments/e330vd/cant_uninstall_games
- https://www.reddit.com/r/Steam/comments/yo81zn/steam_and_drive_is_doubling_storage_space_for_no
- https://www.reddit.com/r/SteamDeck/comments/105py18/components_of_nonsteam_games_and_how_to_properly
- https://www.reddit.com/r/SteamDeck/comments/1jbp4tn/deleted_games_space_left_over
- https://www.reddit.com/r/computers/comments/18vf8pk/where_is_all_my_storage_steam_is_only_135_gigs
- https://www.reddit.com/r/linux_gaming/comments/17md9yx/steam_says_not_enough_disk_space_even_though
- https://www.reddit.com/r/pchelp/comments/1j76rxb/my_steam_library_is_taking_up_an_obscene_amount
- https://www.reddit.com/r/pcmasterrace/comments/14q9x4p/i_have_deleted_these_games_in_the_actual_files
- https://www.reddit.com/r/steamsupport/comments/14p1ydm/how_to_fully_remove_all_game_files_from_computer
- https://www.reddit.com/r/videogames/comments/17n8jos/can_anyone_help_me_figure_out_what_is_taking_such

### 原始搜索结果（按查询词分组，含标题/链接/摘要）

<details>
<summary>steam leftover game folder after uninstall still exists reddit</summary>

```
Results for query "steam leftover game folder after uninstall still exists reddit":

Result 0: If I delete a game will there be any left over folders?or ...
URL: https://www.reddit.com/r/Steam/comments/9hmqob/if_i_delete_a_game_will_there_be_any_left_over/
Depends on the game, but you can always just look in the install folder after uninstalling the game and delete whatever files are left. ...

---

Result 1: Deleted steam games still taking up lots of disk space in ...
URL: https://www.reddit.com/r/Steam/comments/18425ma/deleted_steam_games_still_taking_up_lots_of_disk/
I was trying to look into why my 2tb ssd was showing as having very limited space even though I didn't have many games installed and it wasn't my ...

---

Result 2: Uninstalled Steam game leaves behind over 50GB of data ...
URL: https://www.reddit.com/r/Steam/comments/1atic0t/uninstalled_steam_game_leaves_behind_over_50gb_of/
If you have uninstalled the game through steam, then you are free to delete that folder. There shouldn't be anything using that folder anymore.

---

Result 3: how to fully remove all game files from computer
URL: https://www.reddit.com/r/steamsupport/comments/14p1ydm/how_to_fully_remove_all_game_files_from_computer/
i was playing ark survival evolved, and i noticed the game was called ShooterGame in the task manager for some reason so i renamed it to ARK, then everything ...

---

Result 4: Will uninstalling games delete my progress? If i reinstall them
URL: https://www.reddit.com/r/Steam/comments/1d06zwq/will_uninstalling_games_delete_my_progress_if_i/
Some games without Steam Cloud support will still leave save files even after deletion (TWD Definitive Edition for example).

---

Result 5: i have deleted these games in the actual files but they still show ...
URL: https://www.reddit.com/r/pcmasterrace/comments/14q9x4p/i_have_deleted_these_games_in_the_actual_files/
tl,dr: try Disk Cleanup, And let the Windows program clean up. The entries will probably still be in the list, but the hard disk space should ...

---

Result 6: Installed games are appearing as uninstalled
URL: https://help.steampowered.com/en/faqs/view/4578-18A7-C819-8620
Games may appear uninstalled when Steam no longer recognizes the installation files. As long as the affected game's folder is still in the proper location, ...

---

Result 7: What exactly would mess up if I delete a game folder from ...
URL: https://www.reddit.com/r/Steam/comments/11pkdm8/what_exactly_would_mess_up_if_i_delete_a_game/
Garrys Mod and Fallout 4 take up about 100 GB all together even while uninstalled and would like to remove the folders but not sure if it will mess ...
```
</details>

<details>
<summary>steamapps common folder not deleted after uninstall reddit</summary>

```
Results for query "steamapps common folder not deleted after uninstall reddit":

Result 0: Is safe to delete folder from the \steamapps\common? I ...
URL: https://www.reddit.com/r/Steam/comments/6akpvm/is_safe_to_delete_folder_from_the_steamappscommon/
Occasionally there will be files left over from an uninstall, you can safely delete these, be careful not to delete a game you have installed as ...

---

Result 1: Can I delete steamapps > common files? : r/Steam
URL: https://www.reddit.com/r/Steam/comments/1ck4wq3/can_i_delete_steamapps_common_files/
anything under common is your game files. yes you can delete it. it will only remove the base game files. There will be residual files if you ...

---

Result 2: Can you delete uninstalled games from steamapps ...
URL: https://www.reddit.com/r/Steam/comments/1iczelq/can_you_delete_uninstalled_games_from_steamapps/
Chances are, a lot of what is not being uninstalled is custom content. the uninstall will not remove those files. You would need to manually ...

---

Result 3: Is it fine for me to delete the game files inside common ...
URL: https://www.reddit.com/r/Steam/comments/1kj5nsj/is_it_fine_for_me_to_delete_the_game_files_inside/
I was twerking around on my laptop because my laptop ssd is almost full and found out that steam got 40gb of game data although I don't have any games ...

---

Result 4: Deleted steam games still taking up lots of disk space in ...
URL: https://www.reddit.com/r/Steam/comments/18425ma/deleted_steam_games_still_taking_up_lots_of_disk/
I was trying to look into why my 2tb ssd was showing as having very limited space even though I didn't have many games installed and it wasn't my ...

---

Result 5: Can I uninstall steam and keep my games folder?
URL: https://www.reddit.com/r/Steam/comments/19z535/can_i_uninstall_steam_and_keep_my_games_folder/
Yes. Back up "C:\Program Files (x86)\Steam\steamapps\common" then restore it once Steam is reinstalled.

---

Result 6: Is it okay for me to delete the game files inside common ...
URL: https://www.reddit.com/r/Steam/comments/9hfwjj/is_it_okay_for_me_to_delete_the_game_files_inside/
No problem at all. Best approach is to uninstall from Steam client then check the common folder and delete the game's folder if it's still there ...

---

Result 7: What will happen if I deleted the folders inside my ...
URL: https://steamcommunity.com/discussions/forum/1/3203745634163257147/
Many games keep their folder inside the "steamapps/common" one but they are empty after uninstalling. So they dont really take up any space.
```
</details>

<details>
<summary>orphaned steam game files disk space reddit</summary>

```
Results for query "orphaned steam game files disk space reddit":

Result 0: Steam says not enough disk space even though there is...
URL: https://www.reddit.com/r/linux_gaming/comments/17md9yx/steam_says_not_enough_disk_space_even_though/
The game is on my home directory and i have 40 gb of free space right now. I tried restarting steam, clearing downloading folder in steamapps, ...

---

Result 1: r/Steam - I can't seem to find what is taking up all this space ...
URL: https://www.reddit.com/r/Steam/comments/1kxdifi/i_cant_seem_to_find_what_is_taking_up_all_this/
Run Disk Cleanup and select "Clean up system files" to show all options. It will tell you what Windows OS data can be safely cleaned up ...

---

Result 2: why does steam keep telling me there isnt enough storage?
URL: https://www.reddit.com/r/Steam/comments/1frzpi5/why_does_steam_keep_telling_me_there_isnt_enough/
I think the actual issue is that steam needs space to unpack the encrypted download as well so for a short time it can use double the requested ...

---

Result 3: Steam (And Drive) is doubling storage space for no ...
URL: https://www.reddit.com/r/Steam/comments/yo81zn/steam_and_drive_is_doubling_storage_space_for_no/
The issue isn't that the Steam Library is occupying 150 GBs, it's that the drive claims it has nearly 300 GBs occupied despite the files only ...

---

Result 4: Components of Non-Steam Games and How To Properly ...
URL: https://www.reddit.com/r/SteamDeck/comments/105py18/components_of_nonsteam_games_and_how_to_properly/
There seems to be some confusion about non-steam games, orphaned files and folders and how the dang OTHER storage gets so full so fast.

---

Result 5: Where is all my storage? Steam is only 135 gigs, and ...
URL: https://www.reddit.com/r/computers/comments/18vf8pk/where_is_all_my_storage_steam_is_only_135_gigs/
Go to your steam folder and check there how big it is. see your steam library path is in Program Files x86... You also may want to run Disk ...

---

Result 6: How To Fix Steam Not Enough Free Disk Space Error on ...
URL: https://www.youtube.com/watch?v=WjKAoAkTY6k
How to verify game files and change Steam's download location if the default drive is full; Tips to move or resize your Steam Library and ...

---

Result 7: How To Fix Steam Not Enough Disk Space Error [2025 Full ...
URL: https://www.youtube.com/watch?v=Lg8FtclWsc4
How To Fix Steam Not Enough Disk Space Error [2025 Full Guide] In today's video we will show you how to fix steam not enough disk space ...

---

People Also Ask:
Q: How can I fix the "Steam game not enough disk space" error?
Q: Why is my deleted Steam game still taking up storage space?
Q: Why is Steam using 100% of my disk space?
```
</details>

<details>
<summary>steam uninstall doesn't remove folder reddit</summary>

```
Results for query "steam uninstall doesn't remove folder reddit":

Result 0: I can't delete games on my computer : r/Steam
URL: https://www.reddit.com/r/Steam/comments/1ljssyf/i_cant_delete_games_on_my_computer/
In a pinch you could try manually removing. Just browse to the folder the game is in and remove the whole folder by right clicking and deleting.

---

Result 1: Uninstalled Games still taking up Disk Space : r/Steam
URL: https://www.reddit.com/r/Steam/comments/11wegqe/uninstalled_games_still_taking_up_disk_space/
Check your steam/steamapps/common/ folder and delete folders for games you have uninstalled and use windirstat to see if steam if hiding files ...

---

Result 2: Can I uninstall steam just to reinstall but not lose my games?
URL: https://www.reddit.com/r/Steam/comments/8ysbiv/can_i_uninstall_steam_just_to_reinstall_but_not/
If you have the Steamapps folder on a different location than the Steam.exe and full directory, the steamapps folder will never get deleted.

---

Result 3: Steam doesn't uninstall games completely : r/NobaraProject
URL: https://www.reddit.com/r/NobaraProject/comments/1s3d0nx/steam_doesnt_uninstall_games_completely/
Steam sometimes just takes a while to detect the free space. The games probably deleted just fine. Restart Steam and see if the space got freed ...

---

Result 4: Can't uninstall games : r/Steam
URL: https://www.reddit.com/r/Steam/comments/e330vd/cant_uninstall_games/
Try going into your Steam library folder and clicking repair on the missing drive. It might force steam to figure out the library is gone.

---

Result 5: Wife uninstalled steam before uninstalling games
URL: https://www.reddit.com/r/Steam/comments/1bsen7p/wife_uninstalled_steam_before_uninstalling_games/
You can actually just delete the folder even move games in and out of the folder steam will work out the changes. it's simply watching the ...

---

Result 6: r/Steam Wiki: Troubleshooting Games Randomly Uninstalling
URL: https://www.reddit.com/r/Steam/wiki/steamgamesuninstalling/
Exit Steam. Head to the Steam Program Files folder. Delete everything within the folder except for Steam.exe and the steamapps folder. To ...

---

Result 7: Can you delete uninstalled games from steamapps ...
URL: https://www.reddit.com/r/Steam/comments/1iczelq/can_you_delete_uninstalled_games_from_steamapps/
That all accumulates, and the uninstall will not remove those files. You would need to manually clear them out. At least some of the other games ...
```
</details>

<details>
<summary>ghost steam game folder taking space reddit</summary>

```
Results for query "ghost steam game folder taking space reddit":

Result 0: r/Steam - I can't seem to find what is taking up all this space ...
URL: https://www.reddit.com/r/Steam/comments/1kxdifi/i_cant_seem_to_find_what_is_taking_up_all_this/
Solution is just to delete the game. It's a pain in the ass to re-download, but I've found no other solution that's as simple as this. Might be ...

---

Result 1: Uninstalled Games still taking up Disk Space : r/Steam
URL: https://www.reddit.com/r/Steam/comments/11wegqe/uninstalled_games_still_taking_up_disk_space/
Hey all, I have a 512GB SSD labeled as "Local Disk (C:)" and there's almost no space left despite me deleting a lot of the games in my Steam ...

---

Result 2: Why does steam take up so much space? I don't have any ...
URL: https://www.reddit.com/r/Steam/comments/11bxsv9/why_does_steam_take_up_so_much_space_i_dont_have/
Steam takes up just under 1 GB with no games installed. I would suggest navigating to the Steam game install folder and manually delete ...

---

Result 3: Is this supposed to happen? Why does steam take up so ...
URL: https://www.reddit.com/r/Steam/comments/1iyctxi/is_this_supposed_to_happen_why_does_steam_take_up/
You can drill down into the game folders to see exactly what is there, but yeah you can probably just delete them. If you didn't explicitly put ...

---

Result 4: My Steam Library Is Taking Up An Obscene Amount Of ...
URL: https://www.reddit.com/r/pchelp/comments/1j76rxb/my_steam_library_is_taking_up_an_obscene_amount/
I just recently bought a 1TB m.2 drive and its already nearly full. With 249GB of games saved onto it, I feel there is something wrong happening when ...

---

Result 5: Can anyone help me figure out what is taking such an absurd ...
URL: https://www.reddit.com/r/videogames/comments/17n8jos/can_anyone_help_me_figure_out_what_is_taking_such/
Use WinDirStat. It'll tell you exactly what's taking up space and where.

---

Result 6: Steam Games Occupy Too Much Space? 3 Methods to Move ...
URL: https://www.youtube.com/watch?v=Q7OaSp9RKsc
By default, Steam games will be installed in the C drive, which might take up a lot of storage space ... Method 1: Use the Move Install Folder ...

---

Result 7: Uninstalled game, still takes up 50gb in the steam files?
URL: https://www.reddit.com/r/EliteDangerous/comments/14eijz8/uninstalled_game_still_takes_up_50gb_in_the_steam/
A bug from today download a second copy of the 50Gb used by the game, maybe is the ghost folder. Insde Steamapps - EliteDangerous there is a " ...

---

People Also Ask:
Q: Why is my Steam folder taking up so much space?
Q: Why are my uninstalled Steam games still taking up storage space?
Q: Should Steam be on C drive or D drive?
```
</details>

<details>
<summary>steam game folder remains after uninstall gigabytes reddit</summary>

```
Results for query "steam game folder remains after uninstall gigabytes reddit":

Result 0: Uninstalled steam games still using storage.
URL: https://www.reddit.com/r/Steam/comments/1v152ef/uninstalled_steam_games_still_using_storage/
Try to use SpaceSniffer to understand which folders are using space in your disk. You could also use Revo Uninstaller when uninstalling, so it ...

---

Result 1: Steam Games Folders Still exist inside PC after Uninstalling
URL: https://www.reddit.com/r/Steam/comments/18xvz1i/steam_games_folders_still_exist_inside_pc_after/
Most games will leave a folder behind with stuff like configuration files. The folders should only weight a few MB, and these are more than ...

---

Result 2: I uninstalled a game, but I did not get my storage space back
URL: https://www.reddit.com/r/Steam/comments/1jy71uj/i_uninstalled_a_game_but_i_did_not_get_my_storage/
Go to steam folder and delete the RE2R files directly. You can ... Deleted steam games still taking up lots of disk space in steam folder?

---

Result 3: what data is kept after uninstalling a game? : r/Steam
URL: https://www.reddit.com/r/Steam/comments/1kke76u/what_data_is_kept_after_uninstalling_a_game/
Sometimes savegame data, which can be small or big is kept. Some uninstallers ask if they remove saves too others not. If you change any files ...

---

Result 4: Deleted games space left over? : r/SteamDeck
URL: https://www.reddit.com/r/SteamDeck/comments/1jbp4tn/deleted_games_space_left_over/
I've deleted all the games on my steam deck and it still shows that there's left over gigs on game even tho there's ain't non.

---

Result 5: Uninstalling STEAM games eats some GB still
URL: https://www.reddit.com/r/Steam/comments/1bp9rb1/uninstalling_steam_games_eats_some_gb_still/
Cause some files are left behind, go to your steam common folder and delete the game folder from there. Should get your full GB back.

---

Result 6: Deleted steam games still taking up lots of disk space in ...
URL: https://www.reddit.com/r/Steam/comments/18425ma/deleted_steam_games_still_taking_up_lots_of_disk/
I was trying to look into why my 2tb ssd was showing as having very limited space even though I didn't have many games installed and it wasn't my ...

---

Result 7: Uninstalled Games still taking up Disk Space : r/Steam
URL: https://www.reddit.com/r/Steam/comments/11wegqe/uninstalled_games_still_taking_up_disk_space/
I've struggled with this problem for years but I finally solved it. For me it was clogging my hiberfil.sys, remove it w/ admin cmd: powercfg -h ...
```
</details>

## Workshop / Mod 残留检测

### 查询词

- `steam workshop content not deleted after uninstall reddit`
- `steam workshop folder still there after uninstall site:reddit.com`
- `delete steam workshop files after uninstalling game reddit`
- `steam workshop taking up space after uninstall`
- `steamapps workshop content huge folder clean up reddit`
- `steam mods still on disk after uninstall reddit`

### 命中的不重复 Reddit 帖子（20 条）

- https://www.reddit.com/r/CitiesSkylines/comments/1c6jopo/is_there_a_way_to_tell_steam_to_not_uninstall
- https://www.reddit.com/r/Fallout/comments/h9uc0l/mods_are_taking_up_space_but_game_is_uninstalled
- https://www.reddit.com/r/Kenshi/comments/1itg0m9/mod_automatically_deleted_when_removed_from_steam
- https://www.reddit.com/r/SkaterXL/comments/adiaak/will_uninstalling_and_reinstalling_reset_all_mods
- https://www.reddit.com/r/Steam/comments/1blxwkk/is_there_any_way_to_check_how_much_space_my
- https://www.reddit.com/r/Steam/comments/1ho1nz0/how_to_delete_mod_from_steam_games
- https://www.reddit.com/r/Steam/comments/1qlinbl/fix_for_workshop_content_not_downloading_0b_issue
- https://www.reddit.com/r/Steam/comments/1utlgdt/is_there_a_way_to_deinstall_mods_from_steam
- https://www.reddit.com/r/Steam/comments/4esu1m/is_there_a_way_to_change_workshop_download
- https://www.reddit.com/r/Steam/comments/6xewpw/why_is_my_steam_workshop_folder_so_full
- https://www.reddit.com/r/Steam/comments/9s2fmp/i_finally_discovered_why_my_steam_folder_was_so
- https://www.reddit.com/r/Steam/comments/c1sa94/olddeleted_mods_stay_on_disk_when_removing_them
- https://www.reddit.com/r/Steam/comments/il8gik/would_be_nice_if_steam_also_showed_how_much_space
- https://www.reddit.com/r/Steam/comments/nmhm9o/uninstalling_games_which_have_community_workshop
- https://www.reddit.com/r/Steam/comments/p6504l/check_your_steamsteamappsdownloading_folder_for
- https://www.reddit.com/r/Steam/comments/pewwj0/mods_are_still_on_disk_after_deleting_the_game_a
- https://www.reddit.com/r/Steam/comments/pxycv4/can_i_purge_all_my_oscsteamappsworkshopcontent
- https://www.reddit.com/r/nexusmods/comments/k139k3/will_deleting_a_game_auto_delete_its_mods
- https://www.reddit.com/r/projectzomboid/comments/smc9up/unsubbed_from_workshop_mods_still_appearing
- https://www.reddit.com/r/totalwar/comments/uex7dv/i_am_not_subscribed_to_these_mod_anymore_but_they

### 原始搜索结果（按查询词分组，含标题/链接/摘要）

<details>
<summary>steam workshop content not deleted after uninstall reddit</summary>

```
Results for query "steam workshop content not deleted after uninstall reddit":

Result 0: Mods are still on disk after deleting the game a long time ...
URL: https://www.reddit.com/r/Steam/comments/pewwj0/mods_are_still_on_disk_after_deleting_the_game_a/
To my knowledge, steam will not remove mod files when you uninstall the game. It will only remove the game files. As far as mods go, if they are ...

---

Result 1: Can I purge all my OS(C:)/steamapps/workshop/content files?
URL: https://www.reddit.com/r/Steam/comments/pxycv4/can_i_purge_all_my_oscsteamappsworkshopcontent/
The proper way to do this is to unsubscribe from the workshop content you want to remove. Steam should then delete it automatically. If you just ...

---

Result 2: Is there a way to tell Steam to NOT uninstall content ...
URL: https://www.reddit.com/r/CitiesSkylines/comments/1c6jopo/is_there_a_way_to_tell_steam_to_not_uninstall/
Unfortunately, no. There are short-term workarounds such as staying offline, but as soon as Steam syncs again you'll lose that. Steam's TOS mean ...

---

Result 3: Mod automatically deleted when removed from Steam ...
URL: https://www.reddit.com/r/Kenshi/comments/1itg0m9/mod_automatically_deleted_when_removed_from_steam/
I’ve been playing a Genesis run with the humanoid Cat, Rat, and Fox race mod as well as a mod that adds large mantises to the game. Among several other ...

---

Result 4: Uninstalling games which have community workshop ...
URL: https://www.reddit.com/r/Steam/comments/nmhm9o/uninstalling_games_which_have_community_workshop/
It will uninstall all of your workshop mods but you will stay subscribed so whenever you re install the game it will install the mods.

---

Result 5: Will uninstalling and reinstalling reset all mods if I need a ...
URL: https://www.reddit.com/r/SkaterXL/comments/adiaak/will_uninstalling_and_reinstalling_reset_all_mods/
If the folder is still there after you uninstalled, there may be files that weren't removed, including mods, so just delete the entire folder.

---

Result 6: Fix for Workshop content not downloading (0B issue)
URL: https://www.reddit.com/r/Steam/comments/1qlinbl/fix_for_workshop_content_not_downloading_0b_issue/
The fix is to delete the files in the folder. Then reapplying the workshop works. FYI u/Laufabraud43.

---

Result 7: how do you delete workshop items? :: Help and Tips
URL: https://steamcommunity.com/discussions/forum/1/135511455866636001/
Go on your profile, then to your infos (Like screenshots, games, etc) and click on workshop items, go at one item and press the delete button. # ...

---

People Also Ask:
Q: Does uninstalling Steam games delete workshop mods?
Q: How to get rid of Steam workshop content?
Q: Does Steam ban you for using mods?
```
</details>

<details>
<summary>steam workshop folder still there after uninstall site:reddit.com</summary>

```
Results for query "steam workshop folder still there after uninstall site:reddit.com":

Result 0: Mods are still on disk after deleting the game a long time ...
URL: https://www.reddit.com/r/Steam/comments/pewwj0/mods_are_still_on_disk_after_deleting_the_game_a/
To my knowledge, steam will not remove mod files when you uninstall the game. It will only remove the game files. As far as mods go, if they are ...

---

Result 1: Can I purge all my OS(C:)/steamapps/workshop/content files?
URL: https://www.reddit.com/r/Steam/comments/pxycv4/can_i_purge_all_my_oscsteamappsworkshopcontent/
The proper way to do this is to unsubscribe from the workshop content you want to remove. Steam should then delete it automatically. If you just ...

---

Result 2: Old/Deleted Mods stay on disk when removing them? Any ...
URL: https://www.reddit.com/r/Steam/comments/c1sa94/olddeleted_mods_stay_on_disk_when_removing_them/
On your computer, navigate to ..\Steam\steamapps\workshop\content and locate the appID folder. Delete the target appID folder. Go back to your ...

---

Result 3: Is there a way to tell Steam to NOT uninstall content ...
URL: https://www.reddit.com/r/CitiesSkylines/comments/1c6jopo/is_there_a_way_to_tell_steam_to_not_uninstall/
Unfortunately, no. There are short-term workarounds such as staying offline, but as soon as Steam syncs again you'll lose that.

---

Result 4: Unsubbed from workshop mods still appearing?
URL: https://www.reddit.com/r/projectzomboid/comments/smc9up/unsubbed_from_workshop_mods_still_appearing/
Anyone know how to get them to disappear? I didnt see an option to uninstall from the game so i did it from the workshop. I did reboot the game off/on again ...

---

Result 5: Will uninstalling and reinstalling reset all mods if I need a ...
URL: https://www.reddit.com/r/SkaterXL/comments/adiaak/will_uninstalling_and_reinstalling_reset_all_mods/
If the folder is still there after you uninstalled, there may be files that weren't removed, including mods, so just delete the entire folder.

---

Result 6: I am not subscribed to these mod anymore. But they still ...
URL: https://www.reddit.com/r/totalwar/comments/uex7dv/i_am_not_subscribed_to_these_mod_anymore_but_they/
This is an issue with the steam workshop that has existed since the steam workshop has existed. Unsubscribing doesn't remove the mod just flags

---

Result 7: Is there a way to change workshop download directory?
URL: https://www.reddit.com/r/Steam/comments/4esu1m/is_there_a_way_to_change_workshop_download/
The workshop content is on the same drive as the game so moving the game and workshop content to another library folder should work.
```
</details>

<details>
<summary>delete steam workshop files after uninstalling game reddit</summary>

```
Results for query "delete steam workshop files after uninstalling game reddit":

Result 0: Mods are still on disk after deleting the game a long time ...
URL: https://www.reddit.com/r/Steam/comments/pewwj0/mods_are_still_on_disk_after_deleting_the_game_a/
To my knowledge, steam will not remove mod files when you uninstall the game. It will only remove the game files. As far as mods go, if they are ...

---

Result 1: Can I purge all my OS(C:)/steamapps/workshop/content files?
URL: https://www.reddit.com/r/Steam/comments/pxycv4/can_i_purge_all_my_oscsteamappsworkshopcontent/
The proper way to do this is to unsubscribe from the workshop content you want to remove. Steam should then delete it automatically. If you just ...

---

Result 2: Will uninstalling and reinstalling reset all mods if I need a ...
URL: https://www.reddit.com/r/SkaterXL/comments/adiaak/will_uninstalling_and_reinstalling_reset_all_mods/
Yes. Although you might double check the install folder after uninstalling. Steam sometimes leaves files if they weren't put there by steam.

---

Result 3: Will deleting a game auto delete it's mods? : r/nexusmods
URL: https://www.reddit.com/r/nexusmods/comments/k139k3/will_deleting_a_game_auto_delete_its_mods/
I had fallout 4 mods but I deleted fallout 4 without deleting the mods first. Now I can’t manage them ok vortex as fallout 4 is uninstalled. Will they ...

---

Result 4: HOW TO DELETE MOD FROM STEAM GAMES
URL: https://www.reddit.com/r/Steam/comments/1ho1nz0/how_to_delete_mod_from_steam_games/
Uninstall the mods the same way u installed them but in reverse order, then verify the game integrity so any missing original file gets ...

---

Result 5: Uninstalling games which have community workshop ...
URL: https://www.reddit.com/r/Steam/comments/nmhm9o/uninstalling_games_which_have_community_workshop/
Long story short, I want to uninstall games like GMod. If I re-install it, will it still have the workshop content I had before I uninstalled it ...

---

Result 6: How To Delete Workshop Content In Steam
URL: https://www.youtube.com/watch?v=c4WyIvqWfZ4&vl=en
Quick Tutorial On How To Delete Workshop Content On Steam.

---

Result 7: How to Delete Workshop Content on Steam (2026)
URL: https://www.youtube.com/watch?v=bHbFo8UDM-o
Hey friends, in today's short video I'm going to show you exactly how to delete Workshop content on Steam — finding the Workshop folder and ...

---

People Also Ask:
Q: Does uninstalling Steam games delete workshop mods?
Q: Does uninstalling a Steam game remove all files?
Q: Is it safe to delete the Steam workshop folder?
```
</details>

<details>
<summary>steam workshop taking up space after uninstall</summary>

```
Results for query "steam workshop taking up space after uninstall":

Result 0: would be nice if steam also showed how much space the ...
URL: https://www.reddit.com/r/Steam/comments/il8gik/would_be_nice_if_steam_also_showed_how_much_space/
At the time my hdd was 500gb. Today I have 600gb of mods on tts. With 4tb. Since many mods are taken down by dmca or bad host I save everything.

---

Result 1: Unsubscribed workshop mods still taking up space - Arqade
URL: https://gaming.stackexchange.com/questions/397788/unsubscribed-workshop-mods-still-taking-up-space
I've unsubscribed from all of my Arma 3 workshop mods - however they're still taking up space on my computer, and I'm unsure as to how I'd physically get ...

---

Result 2: Games taking up space after being uninstalled
URL: https://steamcommunity.com/discussions/forum/1/4328601129274391876/
1. delete the folders manually after uninstalling. Something like: c:\Games\Steam\steamapps\common\ · 2. Empty the recycle bin · 3. Check what is ...

---

Result 3: How to Clear Space From Steam Workshop Mods [ 2026 GUIDE]
URL: https://www.youtube.com/watch?v=hHBX1ZKdAFk
How to Clear Space From Steam Workshop Mods [ 2026 GUIDE] how to clear space from steam workshop mods, clear steam workshop cache, delete ...

---

Result 4: Why is my steam workshop taking up 50gb of space?
URL: https://linustechtips.com/topic/1044154-why-is-my-steam-workshop-taking-up-50gb-of-space/
Delete the folder. Any mods you are subbed to will be in there, even if the game is not installed.

---

Result 5: Possibility to save disk space by moving workshop folder
URL: https://forum.studio-397.com/index.php?threads/possibility-to-save-disk-space-by-moving-workshop-folder.59125/
If you want to save disk space on drive where rf2 is installed there is a way to move steam workshop content to another drive. Steam creates ...

---

Result 6: Find Why Uninstalled Games Taking up Disk Space
URL: https://www.minitool.com/news/uninstalled-games-taking-up-disk-space.html
How to Fix the Uninstalled Games Still Taking Up Disk Space Issue? · Way 1: Clear the Game's Leftover Files · Way 2: Clean Up System Cache Files.

---

Result 7: How To Fix Steam Workshop Downloader Free Space Left?
URL: https://www.youtube.com/watch?v=277tgQLK9r8
How To Fix Steam Workshop Downloader Free Space Left? In this video I will explain How You Can Fix Steam Workshop Downloader Free Space Left.

---

People Also Ask:
Q: Why are my uninstalled Steam games still taking up storage space?
Q: How to mass uninstall Steam workshop mods?
Q: Does uninstalling Steam games delete workshop mods?
```
</details>

<details>
<summary>steamapps workshop content huge folder clean up reddit</summary>

```
Results for query "steamapps workshop content huge folder clean up reddit":

Result 0: Why is my steam workshop folder so full?
URL: https://www.reddit.com/r/Steam/comments/6xewpw/why_is_my_steam_workshop_folder_so_full/
Ctrl+E will open the folder up in File Explorer. they may be hidden or temporarily extensionless, generally, yes it's safe to delete workshop ...

---

Result 1: I finally discovered why my Steam folder was so large.
URL: https://www.reddit.com/r/Steam/comments/9s2fmp/i_finally_discovered_why_my_steam_folder_was_so/
another way you can solve the "huge steam folder" problem is by deleting all the stuff it uses to install the game but leaves in the folder ...

---

Result 2: Old/Deleted Mods stay on disk when removing them? Any ...
URL: https://www.reddit.com/r/Steam/comments/c1sa94/olddeleted_mods_stay_on_disk_when_removing_them/
On your computer, navigate to ..\Steam\steamapps\workshop\content and locate the appID folder for the game you want to clear. For A Hat In Time ...

---

Result 3: Is there any way to check how much space my workshop ...
URL: https://www.reddit.com/r/Steam/comments/1blxwkk/is_there_any_way_to_check_how_much_space_my/
Probably a dumb question, but I've got a lot of Gmod addons installed on the workshop, and looking to clean em up. It's mostly things like maps and pm's ...

---

Result 4: Check your steam/steamapps/downloading folder for huge ...
URL: https://www.reddit.com/r/Steam/comments/p6504l/check_your_steamsteamappsdownloading_folder_for/
As far as I can recall you can just delete the whole folder--Steam will just recreate it if need be. If it *does* break you can just make a new ...

---

Result 5: Guide :: Workshop Cleanup and file locations
URL: https://steamcommunity.com/sharedfiles/filedetails/?id=3143874436
Before you can try to submit again, you need to remove the existing content from workshop and then clean up the local file residue.

---

Result 6: Can I purge all my OS(C:)/steamapps/workshop/content files?
URL: https://www.reddit.com/r/Steam/comments/pxycv4/can_i_purge_all_my_oscsteamappsworkshopcontent/
The proper way to do this is to unsubscribe from the workshop content you want to remove. Steam should then delete it automatically.

---

Result 7: How to Clear Space From Steam Workshop Mods [ 2026 GUIDE]
URL: https://www.youtube.com/watch?v=hHBX1ZKdAFk
How to Clear Space From Steam Workshop Mods [ 2026 GUIDE] how to clear space from steam workshop mods, clear steam workshop cache, ...

---

People Also Ask:
Q: Can I delete the SteamApps workshop folder?
Q: How to get rid of Steam workshop content?
Q: How to clear workshop cache on Steam?
```
</details>

<details>
<summary>steam mods still on disk after uninstall reddit</summary>

```
Results for query "steam mods still on disk after uninstall reddit":

Result 0: Mods are still on disk after deleting the game a long time ...
URL: https://www.reddit.com/r/Steam/comments/pewwj0/mods_are_still_on_disk_after_deleting_the_game_a/
To my knowledge, steam will not remove mod files when you uninstall the game. It will only remove the game files. As far as mods go, if they are ...

---

Result 1: Will uninstalling and reinstalling reset all mods if I need a ...
URL: https://www.reddit.com/r/SkaterXL/comments/adiaak/will_uninstalling_and_reinstalling_reset_all_mods/
If the folder is still there after you uninstalled, there may be files that weren't removed, including mods, so just delete the entire folder.

---

Result 2: Is there a way to deinstall mods from steam faster i got 200 ...
URL: https://www.reddit.com/r/Steam/comments/1utlgdt/is_there_a_way_to_deinstall_mods_from_steam/
The fastest way is probably to uninstall all your games, delete all remaining local files, and then reinstall. Right click the game Properties ...

---

Result 3: Uninstalling games which have community workshop ...
URL: https://www.reddit.com/r/Steam/comments/nmhm9o/uninstalling_games_which_have_community_workshop/
It will uninstall all of your workshop mods but you will stay subscribed so whenever you re install the game it will install the mods.

---

Result 4: Will deleting a game auto delete it's mods? : r/nexusmods
URL: https://www.reddit.com/r/nexusmods/comments/k139k3/will_deleting_a_game_auto_delete_its_mods/
I had fallout 4 mods but I deleted fallout 4 without deleting the mods first. Now I can’t manage them ok vortex as fallout 4 is uninstalled. Will they ...

---

Result 5: Old/Deleted Mods stay on disk when removing them? Any ...
URL: https://www.reddit.com/r/Steam/comments/c1sa94/olddeleted_mods_stay_on_disk_when_removing_them/
whether it's by clicking "unsubscribe" from the Steam Workshop mod list or clicking the trash-icon for the mod in-game... they seem to remain.

---

Result 6: Mods are taking up space, but game is uninstalled? : r/Fallout
URL: https://www.reddit.com/r/Fallout/comments/h9uc0l/mods_are_taking_up_space_but_game_is_uninstalled/
So, an hour or so before I posted this, I was going through File Manager and seeing which games I didn't play so I could free up space for a different ...

---

Result 7: Files from uninstalled games. :: Help and Tips
URL: https://steamcommunity.com/discussions/forum/1/4361250551848825478/
Steam does not uninstall workshop mods nor mods from 3rd party sites. For workshop mods you have to unsubscribe from them. For 3rd party ...

---

People Also Ask:
Q: Does uninstalling Steam delete mods?
Q: Why are my uninstalled Steam games still taking up storage space?
Q: Does unsubscribing from Steam workshop mods uninstall them?
```
</details>

## 批量卸载 + 空间预览

### 查询词

- `steam bulk uninstall multiple games reddit`
- `uninstall multiple steam games at once reddit`
- `steam no way to select multiple games uninstall reddit`
- `free up disk space uninstall multiple games steam reddit`
- `steam library cleaner batch uninstall tool`
- `steam feature request uninstall several games at once`

### 命中的不重复 Reddit 帖子（14 条）

- https://www.reddit.com/r/EmuDeck/comments/13y5z1l/is_there_a_way_to_mass_delete_non_steam_games
- https://www.reddit.com/r/Steam/comments/118cgv1/mass_remove_games
- https://www.reddit.com/r/Steam/comments/11wegqe/uninstalled_games_still_taking_up_disk_space
- https://www.reddit.com/r/Steam/comments/16p2vw8/is_there_any_way_to_free_up_some_space_from_steam
- https://www.reddit.com/r/Steam/comments/19drgyk/so_is_there_any_way_i_can_remove_multiple_games
- https://www.reddit.com/r/Steam/comments/1cigm91/mass_delete_steam_games_please_help_i_have_4449
- https://www.reddit.com/r/Steam/comments/1kxdifi/i_cant_seem_to_find_what_is_taking_up_all_this
- https://www.reddit.com/r/Steam/comments/1ljssyf/i_cant_delete_games_on_my_computer
- https://www.reddit.com/r/Steam/comments/1su7wn2/how_much_free_space_do_i_need
- https://www.reddit.com/r/Steam/comments/khjspl/is_there_a_way_to_install_or_uninstall_all_games
- https://www.reddit.com/r/Steam/comments/kmabiw/til_you_can_uninstall_multiple_games_at_a_time
- https://www.reddit.com/r/Steam/comments/ok9u23/installingremoving_multiple_games_at_once_click
- https://www.reddit.com/r/SteamDeck/comments/105py18/components_of_nonsteam_games_and_how_to_properly
- https://www.reddit.com/r/computers/comments/1i0lvap/how_can_i_actually_free_up_space_i_deleted

### 原始搜索结果（按查询词分组，含标题/链接/摘要）

<details>
<summary>steam bulk uninstall multiple games reddit</summary>

```
Results for query "steam bulk uninstall multiple games reddit":

Result 0: TIL you can uninstall multiple games at a time : r/Steam
URL: https://www.reddit.com/r/Steam/comments/kmabiw/til_you_can_uninstall_multiple_games_at_a_time/
Installing/Removing multiple games at once: click on the first game, then shift-click on the last game, that's the trick! Also try Clicking on ...

---

Result 1: So, is there any way I can remove multiple games at once ...
URL: https://www.reddit.com/r/Steam/comments/19drgyk/so_is_there_any_way_i_can_remove_multiple_games/
Installing/Removing multiple games at once: click on the first game, then shift-click on the last game, that's the trick! Also try Clicking on ...

---

Result 2: Is there a way to mass delete non steam games from your ...
URL: https://www.reddit.com/r/EmuDeck/comments/13y5z1l/is_there_a_way_to_mass_delete_non_steam_games/
You can mass delete with steam in desktop mode. Click a title in the list, then hold the shift key and click the last title, right click, ...

---

Result 3: Installing/Removing multiple games at once: click on the first ...
URL: https://www.reddit.com/r/Steam/comments/ok9u23/installingremoving_multiple_games_at_once_click/
Installing/Removing multiple games at once: click on the first game, then shift-click on the last game, that's the trick! Also try Clicking on ...

---

Result 4: Mass delete steam games (please help i have 4449 ...
URL: https://www.reddit.com/r/Steam/comments/1cigm91/mass_delete_steam_games_please_help_i_have_4449/
DELETE "SteamLibrary" folder, then launch steam, HOLD SHIFT then click the LAST one at the bottom, this will select all your games, click on ...

---

Result 5: mass remove games : r/Steam
URL: https://www.reddit.com/r/Steam/comments/118cgv1/mass_remove_games/
Installing/Removing multiple games at once: click on the first game, then shift-click on the last game, that's the trick! Also try Clicking on ...

---

Result 6: How to Uninstall & Delete Games all at Once on STEAM ...
URL: https://www.youtube.com/watch?v=5mio23a95IE
Open the Steam client on your computer. Navigate to your Library where all your installed games are listed. Right-click on the game you want to ...

---

Result 7: How to Install and Uninstall Multiple Steam Games at Once
URL: https://www.makeuseof.com/tag/its-easy-to-batch-install-and-uninstall-steam-games/
Select the games you want to install, right-click one of them, and select Manage > Uninstall selected. Make sure you select only installed ...

---

People Also Ask:
Q: Is there a way to uninstall multiple Steam games at once?
Q: How do I uninstall multiple games at once?
Q: Will uninstalling Steam uninstall all games?
```
</details>

<details>
<summary>uninstall multiple steam games at once reddit</summary>

```
Results for query "uninstall multiple steam games at once reddit":

Result 0: TIL you can uninstall multiple games at a time : r/Steam
URL: https://www.reddit.com/r/Steam/comments/kmabiw/til_you_can_uninstall_multiple_games_at_a_time/
Installing/Removing multiple games at once: click on the first game, then shift-click on the last game, that's the trick! Also try Clicking on ...

---

Result 1: So, is there any way I can remove multiple games at once ...
URL: https://www.reddit.com/r/Steam/comments/19drgyk/so_is_there_any_way_i_can_remove_multiple_games/
Installing/Removing multiple games at once: click on the first game, then shift-click on the last game, that's the trick! Also try Clicking on

---

Result 2: Installing/Removing multiple games at once: click on the first ...
URL: https://www.reddit.com/r/Steam/comments/ok9u23/installingremoving_multiple_games_at_once_click/
Installing/Removing multiple games at once: click on the first game, then shift-click on the last game, that's the trick!

---

Result 3: Is there a way to install or uninstall all games in the library ...
URL: https://www.reddit.com/r/Steam/comments/khjspl/is_there_a_way_to_install_or_uninstall_all_games/
Installing/Removing multiple games at once: click on the first game, then shift-click on the last game, that's the trick! Also try Clicking on ...

---

Result 4: Is there a way to mass delete non steam games from your ...
URL: https://www.reddit.com/r/EmuDeck/comments/13y5z1l/is_there_a_way_to_mass_delete_non_steam_games/
Click a title in the list, then hold the shift key and click the last title, right click, remove non-Steam games from library. It'll remove ...

---

Result 5: mass remove games : r/Steam
URL: https://www.reddit.com/r/Steam/comments/118cgv1/mass_remove_games/
Installing/Removing multiple games at once: click on the first game, then shift-click on the last game, that's the trick! Also try Clicking on ...

---

Result 6: How to Uninstall & Delete Games all at Once on STEAM ...
URL: https://www.youtube.com/watch?v=5mio23a95IE
Open the Steam client on your computer. Navigate to your Library where all your installed games are listed. Right-click on the game you want to ...

---

Result 7: How to Install and Uninstall Multiple Steam Games at Once
URL: https://www.makeuseof.com/tag/its-easy-to-batch-install-and-uninstall-steam-games/
The process of uninstalling multiple Steam games. Select the games you want to install, right-click one of them, and select Manage > Uninstall ...

---

People Also Ask:
Q: Can I uninstall multiple games on Steam at the same time?
Q: How do I uninstall multiple games at once?
Q: How do I remove all traces of a Steam game?
```
</details>

<details>
<summary>steam no way to select multiple games uninstall reddit</summary>

```
Results for query "steam no way to select multiple games uninstall reddit":

Result 0: TIL you can uninstall multiple games at a time : r/Steam
URL: https://www.reddit.com/r/Steam/comments/kmabiw/til_you_can_uninstall_multiple_games_at_a_time/
Installing/Removing multiple games at once: click on the first game, then shift-click on the last game, that's the trick! Also try Clicking on ...

---

Result 1: So, is there any way I can remove multiple games at once ...
URL: https://www.reddit.com/r/Steam/comments/19drgyk/so_is_there_any_way_i_can_remove_multiple_games/
Installing/Removing multiple games at once: click on the first game, then shift-click on the last game, that's the trick! Also try Clicking on ...

---

Result 2: Installing/Removing multiple games at once: click on the first ...
URL: https://www.reddit.com/r/Steam/comments/ok9u23/installingremoving_multiple_games_at_once_click/
Installing/Removing multiple games at once: click on the first game, then shift-click on the last game, that's the trick! Also try Clicking on ...

---

Result 3: Is there a way to install or uninstall all games in the library ...
URL: https://www.reddit.com/r/Steam/comments/khjspl/is_there_a_way_to_install_or_uninstall_all_games/
Installing/Removing multiple games at once: click on the first game, then shift-click on the last game, that's the trick! Also try Clicking on

---

Result 4: Mass delete steam games (please help i have 4449 ...
URL: https://www.reddit.com/r/Steam/comments/1cigm91/mass_delete_steam_games_please_help_i_have_4449/
In your Steam library just click Ctrl+ left click to select specific things or Shift+ left click to select in massive bulk. HOLD SHIFT then ...

---

Result 5: mass remove games : r/Steam
URL: https://www.reddit.com/r/Steam/comments/118cgv1/mass_remove_games/
Installing/Removing multiple games at once: click on the first game, then shift-click on the last game, that's the trick! Also try Clicking on ...

---

Result 6: How to Mass Install - Uninstall Games on Steam
URL: https://www.youtube.com/watch?v=FZBzZGFSC8A
Did you know you can mass install - uninstall games on steam?If you want to free up disk space and you have a lot of games this can be quick ...

---

Result 7: I can't delete games on my computer : r/Steam
URL: https://www.reddit.com/r/Steam/comments/1ljssyf/i_cant_delete_games_on_my_computer/
Open Steam again > Library > Right-click the game > "Uninstall". If that doesn't work, try deleting it using Steam in Big Picture Mode or from ...

---

People Also Ask:
Q: Is there a way to uninstall multiple Steam games at once?
Q: Why isn't Steam letting me uninstall games?
Q: How do I uninstall multiple games at once?
```
</details>

<details>
<summary>free up disk space uninstall multiple games steam reddit</summary>

```
Results for query "free up disk space uninstall multiple games steam reddit":

Result 0: Uninstalled Games still taking up Disk Space : r/Steam
URL: https://www.reddit.com/r/Steam/comments/11wegqe/uninstalled_games_still_taking_up_disk_space/
Alternative to WinDirStat is WizTree, it's much faster. Uninstalling Steam games does not free up space.

---

Result 1: r/Steam - I can't seem to find what is taking up all this space ...
URL: https://www.reddit.com/r/Steam/comments/1kxdifi/i_cant_seem_to_find_what_is_taking_up_all_this/
Debloat and manually uninstall/delete anything you don't need anymore … takes up space quickly. You can also use a feature called disc cleaner ...

---

Result 2: How can i ACTUALLY free up space? I deleted several ...
URL: https://www.reddit.com/r/computers/comments/1i0lvap/how_can_i_actually_free_up_space_i_deleted/
. Also look into using a disk space analyzer, freed up so much space removing it. Also use something like Wiztree to go through all your system ...

---

Result 3: How to Free Up Disk Space for Gaming (Steam & More)
URL: https://diskmop.com/blog/free-up-disk-space-for-gaming
To free up disk space for gaming, clear Steam's download cache, delete shader caches and Workshop files for games you no longer play, ...

---

Result 4: Is there any way to free up some space from steam without ...
URL: https://www.reddit.com/r/Steam/comments/16p2vw8/is_there_any_way_to_free_up_some_space_from_steam/
I have a mid end laptop and i need space for more than two games. Steam takes up 101GB currently, 60GB of which i assume is Cyberpunk.

---

Result 5: How much free space do I need 😭 : r/Steam
URL: https://www.reddit.com/r/Steam/comments/1su7wn2/how_much_free_space_do_i_need/
Always leave 10% of your disk unused, and 2x the amount of memory a game will be as installing can use packing and unpacking to make the file ...

---

Result 6: Components of Non-Steam Games and How To Properly ...
URL: https://www.reddit.com/r/SteamDeck/comments/105py18/components_of_nonsteam_games_and_how_to_properly/
If you've been wondering why your uninstalls aren't freeing up space? This video is for you. DISCLAIMER: This video is intended to be ...

---

Result 7: Free Up Disk Space by Cleaning Up After Deleted Video ...
URL: https://www.howtogeek.com/free-up-disk-space-by-cleaning-up-after-deleted-video-games/
Similarly to Steam, you can usually safely remove these files as they will be replaced on a fresh install and save data is typically stored ...

---

People Also Ask:
Q: Does uninstalling Steam games free up disk space?
Q: Is there a way to uninstall multiple Steam games at once?
Q: How to free up disk space for games on Steam?
```
</details>

<details>
<summary>steam library cleaner batch uninstall tool</summary>

```
Results for query "steam library cleaner batch uninstall tool":

Result 0: So, is there any way I can remove multiple games at once ...
URL: https://www.reddit.com/r/Steam/comments/19drgyk/so_is_there_any_way_i_can_remove_multiple_games/
If this is about all FREE games, then solution is as easy as running a Script like https://github.com/IroN404/Steam-License-Remover. Also works ...

---

Result 1: didmar/steam-bulk-uninstaller: TUI app to ...
URL: https://github.com/didmar/steam-bulk-uninstaller
This tool provides a reliable alternative that: Lists all installed games across all Steam library folders Allows batch selection and uninstallation Performs ...

---

Result 2: How to Uninstall Steam Games with 3 Methods on Windows
URL: https://www.iobit.com/en-blog/how-to-uninstall-steam-game.php
IObit Uninstaller is a powerful tool that can help you completely remove the Steam app, Steam games and their associated files from your ...

---

Result 3: How to Uninstall & Delete Games all at Once on STEAM ...
URL: https://www.youtube.com/watch?v=5mio23a95IE
Open the Steam client on your computer. Navigate to your Library. Right-click on the game you want to uninstall. Select Manage and then choose ...

---

Result 4: Is there any way to uninstall multiple games at once?
URL: https://steamcommunity.com/app/1675200/discussions/0/3812908540888872703/
Ever since the new library change you can select multiple games in your library and with a right-click do something to all of them at once.

---

Result 5: Bulk Crap Uninstaller - Remove large amounts of unwanted ...
URL: https://www.bcuninstaller.com/
Bulk Crap Uninstaller (BCUninstaller, BCU) is a free, open source program manager. It excels at removing large amounts of applications with minimal user ...

---

Result 6: Bulk Crap Uninstaller download
URL: https://sourceforge.net/projects/bulk-crap-uninstaller/
Bulk Crap Uninstaller (in short BCUninstaller or BCU) is a free (as in speech and beer) bulk program uninstaller with advanced automation.

---

Result 7: Uninstall Steam
URL: https://help.steampowered.com/en/faqs/view/3C73-90F9-F600-0266
Right-click on Steam and select Delete. Right-click on SteamService and select Delete. Close your Registry Editor.

---

People Also Ask:
Q: How to bulk uninstall Steam?
Q: How to uninstall apps in bulk?
Q: How to do a clean uninstall on Steam?
```
</details>

<details>
<summary>steam feature request uninstall several games at once</summary>

```
Results for query "steam feature request uninstall several games at once":

Result 0: So, is there any way I can remove multiple games at once ...
URL: https://www.reddit.com/r/Steam/comments/19drgyk/so_is_there_any_way_i_can_remove_multiple_games/
Installing/Removing multiple games at once: click on the first game, then shift-click on the last game, that's the trick! Also try Clicking on ...

---

Result 1: How to Uninstall & Delete Games all at Once on STEAM ...
URL: https://www.youtube.com/watch?v=5mio23a95IE
Uninstalling multiple games on Steam can be done in a few ways. Let me guide you through the process: Uninstall via Steam Client: Open the ...

---

Result 2: Is there any way to uninstall multiple games at once?
URL: https://steamcommunity.com/app/1675200/discussions/0/3812908540888872703/
Ever since the new library change you can select multiple games in your library and with a right-click do something to all of them at once.

---

Result 3: How to Install and Uninstall Multiple Steam Games at Once
URL: https://www.makeuseof.com/tag/its-easy-to-batch-install-and-uninstall-steam-games/
Select the games you want to install, right-click one of them, and select Manage > Uninstall selected. ... Make sure you select only installed ...

---

Result 4: [REQUEST] Remove multiple games from Steam account ...
URL: https://github.com/Depressurizer/Depressurizer/issues/226
My problem is that Steam doesn't allow you to remove multiple games from account at once. I want to remove free to play games that I've ...

---

Result 5: Installed games are appearing as uninstalled
URL: https://help.steampowered.com/en/?text=uninstall+games
How should I uninstall Steam? Important:This process will remove Steam and any installed game content from your machine. If reinstalling Steam and Steam games ...

---

Result 6: Can I remove multiple games from steam's wishlist at once?
URL: https://gaming.stackexchange.com/questions/254909/can-i-remove-multiple-games-from-steams-wishlist-at-once
I've got a wishlist with more than 2200 titles. Even opening it is painful and slow. Removing by one reloads the whole page each time. Is there a way ...

---

Result 7: If I delete a game that I've already bought on Steam ...
URL: https://www.quora.com/If-I-delete-a-game-that-Ive-already-bought-on-Steam-and-then-try-to-install-it-again-will-I-have-to-pay-for-it-again-or-will-it-be-free
If I delete a game that I've already bought on Steam and then try to install it again, will I have to pay for it again or will it be free?

---

People Also Ask:
Q: How can I uninstall multiple games on Steam at the same time?
Q: What is the Steam $5 rule?
Q: How do I uninstall multiple games at once?
```
</details>
