<p align="center">
  <img src="design/gheychi-icon.svg" width="84" alt="Gheychi">
</p>

<h1 align="center">Privacy Policy &amp; Data Safety</h1>

<p align="center">
  <b>Gheychi</b> (<code>com.evergreen.gheychiapp</code>) · Open-source Android SMS/MMS manager<br>
  Effective date: 2026-11-27 · License: GNU GPL-3.0
</p>

<p align="center">
  <a href="PRIVACY.fa.md">فارسی</a> ·
  <a href="README.en.md">README</a> ·
  <a href="https://github.com/MahdiyarGHD/sms-spam-dataset">Dataset &amp; model</a> ·
  <a href="https://github.com/MahdiyarGHD/GheychiApp/issues">Issues</a>
</p>

---

> **Summary:** Your SMS messages and contacts stay on your phone. Spam detection runs offline on-device, and text only leaves your phone if you explicitly choose to edit and submit a spam/ham report.

## 🔒 Local-Only Data & On-Device Processing

- **SMS & MMS messages:** Your SMS inbox, conversations, drafts, folders, pinned chats, scheduled sends, trash, and spam quarantine records are stored locally on your device (in Android's Telephony storage and the app's local SQLite database and preferences).
- **On-device spam filtering:** Spam classification runs locally on your phone using an embedded ML.NET model (`SpamModelStore` / `MlNetSpamClassifier`). Incoming and historical messages are **never** automatically uploaded or sent to any server for filtering.
- **Contacts:** Saved contacts are read locally on your device to display contact names and photos and to avoid treating messages from known contacts as spam. Contact names, numbers, and photos never leave your device.
- **No user accounts:** Gheychi has no accounts, sign-in, or cloud sync service.

## 🌐 Network Requests & Optional Data Sharing

Gheychi does not run background telemetry or tracking. Network access (`INTERNET` / `ACCESS_NETWORK_STATE`) is only used for:

### Optional User-Initiated Spam / Not-Spam Reports
- **When it happens:** Only when you explicitly choose to report a message as **Spam** or **Not spam** and confirm sending in the editable preview dialog.
- **What is sent:** Phone numbers and web links are stripped automatically before the preview is shown, and you can edit or delete any personal details (such as names, OTP codes, or addresses) before tapping send. Only the edited message text and its label (`spam` or `ham`) are sent over HTTPS to a Google Form connected to the open-source [sms-spam-dataset](https://github.com/MahdiyarGHD/sms-spam-dataset) project.
- **What is not sent:** Gheychi does not attach your phone number, sender address, contacts, account info, or device advertising/hardware IDs to a report.

### Spam Detection Model Updates
- **When it happens:** Periodically (or when you tap to check in Settings) to check for newer public spam classification models.
- **What happens:** The app fetches `manifest.json` and `.mlnet` model files over HTTPS from the public GitHub repository [MahdiyarGHD/sms-spam-dataset](https://github.com/MahdiyarGHD/sms-spam-dataset). No SMS contents, contacts, or user identifiers are uploaded.

### App Updates (Distribution-Specific)
- **Google Play builds (`GheychiSelfUpdate=false`):** App self-update checks, APK downloading, and `REQUEST_INSTALL_PACKAGES` are disabled at build time. App updates are handled exclusively by Google Play.
- **GitHub APK builds (`GheychiSelfUpdate=true`):** Standalone builds check the public GitHub Releases Atom feed (`MahdiyarGHD/GheychiApp`) and download signed release APKs when requested by the user.

## 📱 Permissions Used

| Permission | Purpose |
|---|---|
| `READ_SMS`, `RECEIVE_SMS`, `SEND_SMS`, `RECEIVE_MMS`, `RECEIVE_WAP_PUSH` | Core default SMS app functionality: reading, receiving, organizing, and sending messages. |
| `READ_CONTACTS` | Displaying contact names/photos and excluding saved contacts from spam filtering (local only). |
| `POST_NOTIFICATIONS` | Showing local message, delivery report, and daily spam summary notifications. |
| `SCHEDULE_EXACT_ALARM`, `RECEIVE_BOOT_COMPLETED` | Delivering scheduled messages and daily spam summary notifications on time and restoring alarms after reboot. |
| `INTERNET`, `ACCESS_NETWORK_STATE` | Downloading public spam model updates and submitting optional user-confirmed spam/ham reports. |
| `REQUEST_INSTALL_PACKAGES` | **Not included in Google Play builds.** Used only in standalone GitHub APK builds for in-app updates. |

## 🛡️ Ads, Analytics & Third-Party Trackers

- Gheychi contains **no advertisements**, **no analytics SDKs**, and **no crash-reporting or behavioral tracking SDKs**.
- Links you choose to tap inside SMS messages or Settings (such as Help & feedback, Privacy policy, or Open-source licenses) open in your device's browser.

## 🗑️ Data Retention & Control

- **On-device data:** You can clear saved search queries and all quarantined spam records anytime in **Settings → Privacy & storage**, delete individual messages or empty Trash, or uninstall the app to remove its local database and preferences.
- **Voluntary spam reports:** Because submitted reports do not include user or device identifiers, individual anonymous entries sent to the public dataset cannot be tied back to you or removed after submission.

## 📊 Google Play Data Safety Summary

| Data Safety Category | Collected? | Shared with Third Parties? | Details |
|---|---|---|---|
| **Messages (Other in-app messages)** | Optional (user-initiated only) | No | Sent over HTTPS only if you explicitly edit and submit a spam/ham report to improve the open-source spam dataset. Not linked to your identity. |
| **Contacts** | No | No | Read locally on-device only; never uploaded. |
| **Personal info / Financial / Location** | No | No | Not collected. |
| **Device or other IDs** | No | No | Not collected. |
| **App activity / Crash logs / Diagnostics** | No | No | Not collected. |

- **Data encryption in transit:** Network requests (model downloads from GitHub and optional user-submitted reports to Google Forms) use HTTPS.

## 📬 Contact

If you have questions about this policy or want to report a privacy issue, please open an issue at [github.com/MahdiyarGHD/GheychiApp/issues](https://github.com/MahdiyarGHD/GheychiApp/issues).
