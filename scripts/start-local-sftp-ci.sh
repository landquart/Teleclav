#!/usr/bin/env bash
# Isolated loopback OpenSSH for the integration test on a disposable runner.
set -euo pipefail
: "${RUNNER_TEMP:?}" "${GITHUB_ENV:?}"
root="$RUNNER_TEMP/teleclav-sftp"
mkdir -m 700 "$root"
ssh-keygen -q -t ed25519 -N '' -f "$root/host_key"
ssh-keygen -q -t ed25519 -N '' -f "$root/client_key"
cp "$root/client_key.pub" "$root/authorized_keys"
chmod 600 "$root/authorized_keys"
user_name="$(id -un)"
cat > "$root/sshd_config" <<CONFIG
Port 22222
ListenAddress 127.0.0.1
HostKey $root/host_key
PidFile $root/sshd.pid
AuthorizedKeysFile $root/authorized_keys
StrictModes no
PasswordAuthentication no
KbdInteractiveAuthentication no
PubkeyAuthentication yes
PermitRootLogin no
UsePAM yes
AllowUsers $user_name
Subsystem sftp internal-sftp
CONFIG
sudo mkdir -p /run/sshd
sudo /usr/sbin/sshd -t -f "$root/sshd_config"
sudo /usr/sbin/sshd -f "$root/sshd_config" -E "$root/sshd.log"
printf '[127.0.0.1]:22222 %s\n' "$(cat "$root/host_key.pub")" > "$root/known_hosts"
printf 'pwd\n' | sftp -q -b - -P 22222 -i "$root/client_key" \
    -o BatchMode=yes -o StrictHostKeyChecking=yes -o "UserKnownHostsFile=$root/known_hosts" \
    "$user_name@127.0.0.1"
fingerprint="$(ssh-keygen -lf "$root/host_key.pub" -E sha256 | awk '{print $2}')"
printf 'TELECLAV_TEST_SFTP_PORT=22222\nTELECLAV_TEST_SFTP_KEY=%s\nTELECLAV_TEST_SFTP_HOST_KEY=%s\nTELECLAV_TEST_SFTP_USER=%s\n' \
    "$root/client_key" "$fingerprint" "$user_name" >> "$GITHUB_ENV"
