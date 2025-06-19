# SteamCollectionDownloader

## Step 1: Prepare SteamCMD

-Download SteamCMD for Windows:

-https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip

-Create a folder for SteamCMD, here: ***D:\steamcmd.*** (For now it is hardcoded path)

-Extract the contents of the ZIP file into the folder.

--On the first run, several folders and files will be created automatically.

## Step 2: Use SteamCollectionDownloader

-Run SteamDownloader.

-Insert the collection URL.

-SteamCMD will operate in the background and will be downloading items one by one (***SteamCollectionDownloader must remain OPEN or downloading will be stopped!!.***).

-You can track the download progress in:
**D:\steamcmd\steamapps\workshop\content\ {gameID}**

Or after every batch (10 items) in SteamCollectionDownloader console


![{36AFD741-43E8-4979-B47E-C34405026A73}](https://github.com/user-attachments/assets/dd2168c8-07c7-48bb-81e7-cccdd5791114)






-If you want stop downloading items just stop SteamCollectionDownloader.



# Problems

- If you are experiencing any issues with failed downloading,try delete : D:\steamcmd\steamapps\workshop/appworkshop_{gameID}.acf 

