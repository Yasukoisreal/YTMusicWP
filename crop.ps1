Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile("d:\Documents\Visual Studio 2015\Projects\YTMusicWP\Pictures\donate_qr.jpg")
$bmp = new-object System.Drawing.Bitmap($img)
$cropRect = New-Object System.Drawing.Rectangle(30, 145, 400, 440)
$croppedBmp = $bmp.Clone($cropRect, $bmp.PixelFormat)
$img.Dispose()
$croppedBmp.Save("d:\Documents\Visual Studio 2015\Projects\YTMusicWP\Pictures\donate_qr.jpg", [System.Drawing.Imaging.ImageFormat]::Jpeg)
$croppedBmp.Dispose()
$bmp.Dispose()
