# WinBatService
 Run bat file as Windows service

 Example:
	sc.exe create cam1 displayname= "cam1" binpath= "PATH TO: WinBatService.exe --batFile=PATH TO: BatchFile.bat" start= auto obj= .\localUser password= localUserPassword depend= NetBIOS