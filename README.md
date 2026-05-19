# J-Runner with Extras
The Ultimate RGH/JTAG App

System Requirements:
- x86 based Windows PC (i386 or amd64)
- Windows Vista SP2 or later
- dotNET Framework 4.5.2
- USB 2.0 port for hardware devices

Docker integration (experimental and limited to PicoFlasher only).
- User must be in dialout group.
- Before starting container, PicoFlasher must be present in the system.

```
mkdir workdir
docker run -it --network=host \
--group-add=keep-groups \
--device=/dev/ttyACM0 \
-e DISPLAY=$DISPLAY \
-v`pwd`/workdir:/root/workdir \
-v /tmp/.X11-unix:/tmp/.X11-unix ghcr.io/j-runner-with-extras/j-runner:latest
```

[Download Latest Stable Package](https://github.com/J-Runner-With-Extras/J-Runner-with-Extras/releases/latest)