# Menandatangani installer (opsional)

Installer rilis awal tidak ditandatangani. Penandatanganan memerlukan sertifikat **Code Signing** yang tepercaya dan memiliki private key yang tersedia, Windows SDK dengan `signtool.exe` resmi Microsoft, serta URL server timestamp RFC 3161 yang mendukung HTTPS. Sertifikat dapat disimpan di toko sertifikat Windows pengguna saat ini atau Local Machine. Penandatanganan tidak menjamin semua peringatan SmartScreen langsung hilang.

1. Bangun installer final menggunakan `build-installer.ps1`.
2. Dapatkan thumbprint sertifikat yang akan digunakan. Jangan bagikan private key, PIN token, atau kata sandi.
3. Dari PowerShell, jalankan dengan lokasi dan URL milik Anda:

```powershell
powershell -ExecutionPolicy Bypass -File duodesk/installer/sign-installer.ps1 `
  -InstallerPath 'duodesk/dist/HY-Duo-WhatsApp-Setup.exe' `
  -SignToolPath 'C:\Path\to\Windows SDK\signtool.exe' `
  -Thumbprint '0123456789ABCDEF0123456789ABCDEF01234567' `
  -TimestampUrl 'https://timestamp.example.com'
```

Ganti semua nilai contoh, khususnya URL timestamp dan thumbprint, dengan nilai yang sesuai. Untuk sertifikat di toko Local Machine, tambahkan `-MachineStore`. Untuk memeriksa format parameter dan keberadaan file tanpa membuka sertifikat, mengeksekusi signtool, atau mengubah installer, tambahkan `-ValidateOnly`.

Skrip memeriksa tanda tangan Microsoft pada signtool, kegunaan sertifikat, private key, masa berlaku dan URL timestamp HTTPS. Setelah menandatangani dengan SHA-256 dan timestamp RFC 3161, skrip memverifikasi tanda tangan. Jika sebuah langkah gagal, jangan distribusikan installer sampai penyebabnya diperiksa dan verifikasi ulang berhasil. Tidak ada sertifikat atau kredensial yang disertakan dalam repositori.
