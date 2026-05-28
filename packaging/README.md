# Packaging

## systemd user service

`systemd/wipelog.service` runs the daemon as a per-user service so it
auto-starts on login and restarts on failure. Tied to
`graphical-session.target` so it only runs while you're logged in to a
desktop — it does not start during boot when no display is available.

### Install

1. Publish the daemon (and optionally the app) into `~/bin/wipelog` /
   `~/bin/wipelog-app`:

   ```sh
   dotnet publish src/Wipelog.Daemon -c Release --no-self-contained \
       -o ~/bin/wipelog
   dotnet publish src/Wipelog.App    -c Release --no-self-contained \
       -o ~/bin/wipelog-app
   ```

2. Copy the unit file into your systemd user dir:

   ```sh
   mkdir -p ~/.config/systemd/user
   cp packaging/systemd/wipelog.service ~/.config/systemd/user/
   ```

3. Edit `ExecStart` in `~/.config/systemd/user/wipelog.service` to
   match your actual log directory and output directory. The default
   `--log-dir` assumes a stock `~/.wine` prefix — adjust for Lutris,
   Heroic, Bottles, or a different output target. The GUI has a
   "Detect WoW logs" button that scans common launcher layouts.

4. Reload, enable, and start:

   ```sh
   systemctl --user daemon-reload
   systemctl --user enable --now wipelog.service
   ```

5. Verify the service is healthy:

   ```sh
   systemctl --user status wipelog.service
   journalctl --user -u wipelog.service -f
   ```

The daemon writes a heartbeat to `~/.cache/wipelog/status.json` every
two seconds. If the file is missing or its `heartbeatAt` is older than ten
seconds, the GUI treats the daemon as offline.

### Optional install helper

`packaging/install.sh` automates steps 2-4 and prompts for log / output
directories. Run it after step 1 if you'd rather skip the manual edit:

```sh
./packaging/install.sh
```

### Manual verification

systemd units are awkward to unit-test, so verify by hand after install:

- `systemctl --user is-active wipelog.service` returns `active`.
- Logging out of the desktop session stops the service (`PartOf`).
- Killing the daemon (`kill <pid>`) triggers a restart within 5 seconds
  (`Restart=on-failure`).
- A fresh `~/.cache/wipelog/status.json` appears within a few seconds
  of `systemctl --user start`.

## Notifications

The daemon shells out to `notify-send` (from `libnotify`) for clip-saved
and error toasts. If `notify-send` isn't installed the daemon logs a
single warning and continues silently. To opt out without uninstalling
libnotify, pass `--no-notifications` on the daemon command line.

```sh
# Arch
sudo pacman -S libnotify
# Debian / Ubuntu
sudo apt install libnotify-bin
```
