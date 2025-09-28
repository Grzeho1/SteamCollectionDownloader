# SteamCollectionDownloader

***With this downloader, you can download all items from a Steam Workshop collection at once — not one by one, and even if you don't own the game on Steam.***

Release with .exe here: (https://github.com/Grzeho1/SteamCollectionDownloader/releases/tag/v0.4)

## Step 1: Prepare SteamCMD  (If you already have steamCMD in your computer, u can skip)

-Download SteamCMD for Windows:

-https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip

-Create a folder for SteamCMD, here: ***D:\steamcmd.***  OR ***C:\steamcmd.***  (u can write path manualy later if steamdownloader cant find steamcmd.exe)

-Extract the contents of the ZIP file into the folder.

--On the first run, several folders and files will be created automatically.

## Step 2: Use SteamCollectionDownloader

-Run SteamDownloader.

-Insert the collection URL.

-SteamCMD will operate in the background and will be downloading items one by one (***SteamCollectionDownloader must remain OPEN or downloading will be stopped!!.***).

-You can track the download progress in:
**D:\steamcmd\steamapps\workshop\content\ {gameID}**

Or after every batch (2 items) in SteamCollectionDownloader console


<img width="619" height="593" alt="image" src="https://github.com/user-attachments/assets/5f6848cd-9329-4649-9d0e-64bb2975bbc0" />





-If you want stop downloading items just stop SteamCollectionDownloader.



# Problems

- If you are experiencing any issues with failed downloading,try delete : D:\steamcmd\steamapps\workshop/appworkshop_{gameID}.acf
- 
- If u cant start steamcollectiondownloader.exe u need to download .NET runtime to your computer  https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime
