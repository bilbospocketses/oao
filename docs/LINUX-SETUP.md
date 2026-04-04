# Linux Setup Guide

This guide covers installing and running Open Audio Orchestrator on Linux. For Windows, see [`WINDOWS-SETUP.md`](WINDOWS-SETUP.md).

## Prerequisites

You need: an NVIDIA GPU with CUDA drivers, Docker with NVIDIA Container Toolkit, .NET 9 SDK, and Git with Git LFS.

### Debian / Ubuntu

```bash
# NVIDIA drivers (if not already installed)
sudo apt update
sudo apt install -y nvidia-driver-550

# NVIDIA Container Toolkit
curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | sudo gpg --dearmor -o /usr/share/keyrings/nvidia-container-toolkit-keyring.gpg
curl -s -L https://nvidia.github.io/libnvidia-container/stable/deb/nvidia-container-toolkit.list | \
  sed 's#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-container-toolkit-keyring.gpg] https://#g' | \
  sudo tee /etc/apt/sources.list.d/nvidia-container-toolkit.list
sudo apt update
sudo apt install -y nvidia-container-toolkit

# Docker CE (add official Docker repo first)
sudo apt install -y ca-certificates curl
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io
sudo systemctl enable --now docker
sudo nvidia-ctk runtime configure --runtime=docker
sudo systemctl restart docker

# .NET 9 SDK
sudo apt install -y dotnet-sdk-9.0

# Git + Git LFS
sudo apt install -y git git-lfs
git lfs install
```

### RHEL / Fedora

```bash
# NVIDIA drivers (RPM Fusion or official NVIDIA repo)
sudo dnf install -y akmod-nvidia

# NVIDIA Container Toolkit
curl -s -L https://nvidia.github.io/libnvidia-container/stable/rpm/nvidia-container-toolkit.repo | \
  sudo tee /etc/yum.repos.d/nvidia-container-toolkit.repo
# Fedora 44+ moved the CA bundle — create symlink if missing
if [ ! -f /etc/pki/tls/certs/ca-bundle.crt ]; then
  sudo ln -s /etc/pki/ca-trust/extracted/pem/tls-ca-bundle.pem /etc/pki/tls/certs/ca-bundle.crt
fi
sudo dnf install -y nvidia-container-toolkit

# Docker CE (add official Docker repo first)
sudo dnf install -y dnf-plugins-core
sudo dnf config-manager addrepo --from-repofile=https://download.docker.com/linux/fedora/docker-ce.repo
sudo dnf install -y docker-ce docker-ce-cli containerd.io
sudo systemctl enable --now docker
sudo nvidia-ctk runtime configure --runtime=docker
sudo systemctl restart docker

# .NET 9 SDK
sudo dnf install -y dotnet-sdk-9.0

# Git + Git LFS
sudo dnf install -y git git-lfs
git lfs install
```

### Alpine

> **Note:** Alpine uses musl libc and OpenRC (not systemd). .NET 9 has official Alpine support. NVIDIA driver installation on Alpine is more involved — refer to [NVIDIA's documentation](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html) for your setup.

```bash
# Docker
sudo apk add docker
sudo rc-update add docker default
sudo service docker start

# .NET 9 SDK
sudo apk add dotnet9-sdk

# Git + Git LFS
sudo apk add git git-lfs
git lfs install
```

## Verify GPU Access

After installing prerequisites, verify Docker can access your GPU:

```bash
docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi
```

You should see your GPU listed. If this fails, check that the NVIDIA Container Toolkit is installed and Docker has been restarted.

## Build and Run

```bash
git clone https://github.com/bilbospocketses/OpenAudioOrchestrator.git
cd OpenAudioOrchestrator
dotnet run --project src/OpenAudioOrchestrator.Web
```

Navigate to `http://localhost:5206` and complete the setup wizard. The wizard detects your platform and shows Linux-appropriate defaults.

## Running as a systemd Service

For production deployments, run the app as a systemd service.

### 1. Create a service account

```bash
sudo useradd -r -s /usr/sbin/nologin oao
```

### 2. Publish the app

```bash
dotnet publish src/OpenAudioOrchestrator.Web -c Release -o /opt/OpenAudioOrchestrator/app
sudo chown -R oao:oao /opt/OpenAudioOrchestrator
```

### 3. Add the service user to the docker group

```bash
sudo usermod -aG docker oao
```

### 4. Install the systemd unit

Create `/etc/systemd/system/oao.service`:

```ini
[Unit]
Description=Open Audio Orchestrator
After=network.target docker.service
Requires=docker.service

[Service]
Type=notify
User=oao
WorkingDirectory=/opt/OpenAudioOrchestrator/app
ExecStart=/usr/bin/dotnet OpenAudioOrchestrator.Web.dll
Restart=on-failure
RestartSec=10
Environment=ASPNETCORE_URLS=http://0.0.0.0:5206
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

### 5. Enable and start

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now oao
sudo journalctl -u oao -f   # watch logs
```

Navigate to `http://your-server:5206` and complete the setup wizard.

## Alpine Notes

- Alpine uses **OpenRC**, not systemd. Adapt the service configuration to use OpenRC init scripts, `s6`, or `supervise`.
- NVIDIA driver installation on Alpine differs from Debian/RHEL. Consult the [NVIDIA Container Toolkit docs](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html).
- .NET 9 is officially supported on Alpine (musl). No compatibility issues expected.

## Troubleshooting

**"Permission denied" connecting to Docker:**
Add your user to the `docker` group: `sudo usermod -aG docker $USER`, then log out and back in.

**nvidia-smi not found:**
Ensure NVIDIA drivers are installed and `nvidia-smi` is in your PATH. The app uses this for GPU metrics on the dashboard.

**Setup wizard shows wrong default paths:**
The wizard auto-detects your platform. If it shows Windows paths on Linux, the `PlatformDefaults` detection may have failed — please open an issue.
