<p align="center">
  <a href="https://github.com/Grzeho1/SteamCollectionDownloader/releases/tag/v1.0">
    <img src="https://img.shields.io/badge/release-v1.0-brightgreen?style=for-the-badge&logo=github" alt="Latest Release">
  </a>
  <a href="https://github.com/Grzeho1/SteamCollectionDownloader/releases">
    <img src="https://img.shields.io/github/downloads/Grzeho1/SteamCollectionDownloader/total?style=for-the-badge&color=blue&logo=steam" alt="Downloads">
  </a>
  <a href="https://dotnet.microsoft.com/">
    <img src="https://img.shields.io/badge/Made%20with-C%23-239120?style=for-the-badge&logo=c-sharp" alt="Made with C#">
  </a>
</p>


# SteamCollectionDownloader

***With this downloader, you can download all items from a Steam Workshop collection at once — not one by one, and even if you don't own the game on Steam.***

##  Features
-  Download entire Steam Workshop collections with a single click  
-  Displays item names, progress, and status in a clean console log  
-  Automatically downloads **SteamCMD** if not found  
-  Optional login with your own Steam account (required for workshop items of paid games you own)


##  How to Use

## -  [ CLICK To Download Latest Release](https://github.com/Grzeho1/SteamCollectionDownloader/releases/tag/v1.0)


- 1️ Double-click **`SteamCollectionDownloader.exe`**  
- 2️ Insert the collection URL (e.g. `https://steamcommunity.com/sharedfiles/filedetails/?id=123456789`)  
- 3️ Choose login mode:
  - **Anonymous**-works for workshop items of free-to-play games  
  - **My Steam account**- required for workshop items of paid games you own. Enter username + password. On the first login a dialog will ask for your **Steam Guard code** (sent to your email or from the mobile authenticator app). Subsequent runs reuse the cached session and won't ask again.  
- 4️ Click **Start** to download all items in the collection  

> [!NOTE]  
> Steam Guard code has a short validity (~10 minutes). If the code expires before you enter it, just start the download again and a fresh code will be sent.




> [!TIP]  
> ![Preview](https://github.com/user-attachments/assets/55b13c0e-2f49-4c6f-b1ca-1b02ad38cb5b)




# Problems

- If you are experiencing any issues with failed downloading,try delete : D:\steamcmd\steamapps\workshop/appworkshop_{gameID}.acf

> [!TIP]  
>  If you have any issues or ideas, please let me know!


