<p align="center">
  <img src="design/gheychi-icon.svg" width="120" alt="Gheychi">
</p>

<h1 align="center">Gheychi</h1>

<p align="center">
  <b>Your SMS inbox without the noise.</b><br>
  Spam is caught and set aside right here, on your phone.
</p>

<p align="center">
  <a href="README.md">فارسی</a> ·
  <a href="https://github.com/MahdiyarGHD/GheychiApp/releases/latest">Download the latest release</a> ·
  <a href="https://github.com/MahdiyarGHD/sms-spam-dataset">Dataset &amp; model</a>
</p>

<p align="center">
  <img src="design/badges/android.svg" alt="Android 7.0+">
  <img src="design/badges/avalonia.svg" alt="Avalonia 12">
  <img src="design/badges/mlnet.svg" alt="ML.NET on-device">
  <a href="LICENSE"><img src="design/badges/license.svg" alt="License: GPL-3.0"></a>
</p>

---

## Why Gheychi?

Every day brings a pile of ads, lotteries and unwanted texts. Each one buzzes with its own notification, pulls your attention away and breaks your focus, and the messages that matter get lost between them.

Gheychi ("scissors" in Persian) is a full SMS app for Android that recognises these messages with a machine-learning model **running on the phone itself** and quietly moves them to a Spam tab. No notification, no sound. Your messages are never sent to a server to be checked.

## ✂️ Key features

### On-device spam detection
- An ML.NET model that runs fully offline on the phone.
- Spam never notifies or makes a sound, so it never breaks your focus.
- Three sensitivity levels (relaxed, balanced, strict) with an adjustable threshold.
- Saved contacts are never checked; any other sender can be marked as trusted.
- Old spam is cleared automatically after a period you choose.
- The detection model updates from inside the app, without installing a new version.
- A quiet daily summary ("X spam caught today") at a time you pick; tapping it opens the Spam tab.
- Spam analytics: counts over several periods, a 14-day chart, confidence, accuracy and top senders.
- Report a wrong call ("spam" / "not spam") and edit the text before it is sent, so anything personal can be taken out.

### A complete SMS app
- Full **multi-SIM** support: the receiving SIM on each message, a default SIM, and a fixed SIM per conversation.
- Emoji reactions, compatible with iPhone and Google Messages reactions.
- Archive conversations with a horizontal swipe.
- Fast search with filters for unread, starred, known, unknown, links and places, plus voice search.
- A profile for each conversation with its shared links, notification and SIM settings.
- Snooze a conversation (1 hour, 8 hours, until tomorrow morning, 1 week, or until turned back on).
- Reply straight from the notification, and hide message content on the lock screen.

### Design
- A fully **Persian right-to-left** interface, and English.
- Built to feel smooth: full AOT compilation, incremental loading and native animations.

## 🧠 Model & dataset

The spam model is trained in a separate repository, [sms-spam-dataset](https://github.com/MahdiyarGHD/sms-spam-dataset). The SMS dataset, the training code and every model version are published there, and Gheychi downloads new models from it.

Reports sent from the app help improve the dataset and future versions of the model.

## 🔒 Privacy

- Spam detection happens entirely on the phone; message text is never sent anywhere to be checked.
- Text leaves the phone only when you report a message yourself, after you have seen and edited it.
- Checking for app and model updates only reads public GitHub files.

## 📲 Install

- **Requires** Android 7.0 or newer.
- Download `Gheychi-x.y.z.apk` from the [releases page](https://github.com/MahdiyarGHD/GheychiApp/releases/latest) and install it; it runs on any phone. The smaller APKs each target one processor type: `arm64-v8a` for most phones, `armeabi-v7a` for older ones.
- On first launch, set Gheychi as the default SMS app: Android lets only the default SMS app receive and manage messages.
- New versions are checked for automatically once a day.

## 🛠️ Building from source

Requirements: .NET 10 SDK and the Android workload (`dotnet workload install android`) and a JDK 17.

```bash
git clone https://github.com/MahdiyarGHD/GheychiApp.git
cd GheychiApp
dotnet build src/Gheychi.App/Gheychi.App.csproj -c Release -f net10.0-android
```

The spam model is not kept in the repository. The first build downloads its latest version from the [sms-spam-dataset](https://github.com/MahdiyarGHD/sms-spam-dataset) repository.

Running the tests:

```bash
dotnet test tests/Gheychi.Core.Tests
```

## 🧩 Project layout

| Folder | What's in it |
|---|---|
| `src/Gheychi.App` | The Avalonia app: pages, controls and Android-specific code |
| `src/Gheychi.Core` | Platform-independent logic: spam decisions, search, dates, notifications and updates |
| `src/Gheychi.Infrastructure` | The ML.NET model, SQLite storage, update sources and reports |
| `tests/` | Unit tests for Core and Infrastructure |

## 📄 License

Copyright © 2026 MahdiyarGHD. Gheychi is free software under the [GNU General Public License v3.0](LICENSE): you can use, study, change and share it, but any copy or modified version you distribute must stay open source, with its source code, under the same license.

---

<p align="center">Made with 💚 for a calmer inbox</p>
