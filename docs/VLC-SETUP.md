# VLC setup

First play a video and check CinePresence → Sources. If VLC already appears with title and timing, Windows integration may be sufficient.

For VLC 3.x, enable a local HTTP connection:

1. In VLC, open **Tools → Preferences**. Under **Show settings**, choose **All**.
2. Open **Interface → Main interfaces** and enable **Web**. Preserve any existing enabled interfaces.
3. Open **Main interfaces → Lua** and choose a strong **Lua HTTP password**. Save and fully close VLC.
4. Make a backup of `%APPDATA%\vlc\vlcrc`. In that file, find the HTTP host and port settings and set their uncommented values to:

   ```ini
   http-host=127.0.0.1
   http-port=8080
   ```

   Preserve every other setting. A leading `#` comments out a setting. Do not configure the interface to listen on all network addresses. CinePresence itself only connects to `127.0.0.1`.
5. Restart VLC and play a video.
6. In CinePresence → Settings, enable **VLC HTTP adapter**, enter the same port and password, click **Test VLC connection**, then **Save settings**.

If port 8080 is in use, choose another unused port and set the same value in both applications. CinePresence reads `/requests/status.json` using HTTP Basic authentication; it does not send playback commands or change VLC configuration.

Only one HTTP endpoint is configured at a time. When VLC's Windows session and HTTP adapter report the same title/episode, CinePresence favors the HTTP timing and retains Windows' focus information. Separate VLC instances playing different titles remain separate sources if Windows exposes them.

If the connection fails, check that VLC is running, the Web interface is enabled, the password matches, and the port is correct. No external firewall opening is required for a loopback-only connection.
