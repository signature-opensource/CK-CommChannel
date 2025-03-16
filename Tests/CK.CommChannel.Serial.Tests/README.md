# CK.CommChannel.Serial.Tests

Tests for the `System.IO.Ports.SerialPort` implementation of `CommunicationChannel`.

These tests **require** a virtual COM port pair to be installed on the system, which isn't exactly something that can be done automatically on Windows since it involves drivers. On Linux or Unix-like systems, you can use `socat` (also available on macOS with brew or MacPorts).

Once installed, you also need to tell the tests which COM ports to use - this is done with environment variables.

## Setting up a Virtual COM port pair on Windows

These tests were made using version 2.2.2.0 of `com0com` drivers, signed by Pete Batard (Akeo Consulting). You can find more information about the driver and its signer [here](https://pete.akeo.ie/2011/07/com0com-signed-drivers.html).

1. Download com0com: [original](http://files.akeo.ie/blog/com0com.7z), or [mirror](https://invenietisparis.sharepoint.com/:u:/g/EZy955qUmthHr6bKS4NiSzIBysZtYiKS6l378xjm0atCQA?e=6eqqxP)
2. Unzip the file, navigate to the `x64` directory, and open a command line prompt as Administrator within
3. Run `setupc.exe install 1 PortName=COM10 PortName=COM11` to install a virtual COM port pair, with number 1, and COM Port names COM10 and COM11.
   - Note: Change `COM10` and `COM11` as needed, depending on COM port usage in your system. You can view and clear COM ports reserved by Windows using the Registry, or with tools suck as [ComNameArbiterTool](https://www.uwe-sieber.de/files/ComNameArbiterTool.zip).
4. Verify that the driver signature reads "*Akeo Consulting*", and accept the driver installation.

Once installed, the virtual COM port pair will show up in the Device Manager in the *com0com - serial port emulators* section. You can uninstall them using `setupc.exe uninstall`, or using the Device Manager. Active COM ports can be listed with the windows command `mode`.

## Setting up environment variables

The environment variables used by the tests are the following:

- `CK_COMMCHANNEL_TESTS_SERIAL_PORT_A`
- `CK_COMMCHANNEL_TESTS_SERIAL_PORT_B`

Those need to be set to each of the COM port names used by the virtual COM port pair - eg. to `COM10` and `COM11` using the above command (or eg. `/dev/pts/3` and `/dev/pts/4` on Linux or Unix-like systems).

You can change environment variables [on Windows](https://www.google.com/search?q=windows+change+environment+variables) easily enough, but **remember to close Visual Studio (or any command line shell) and reopen it after changing environment variables**. If you don't do this, the test runner from within Visual Studio will not find them.