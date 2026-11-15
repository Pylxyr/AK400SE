## Install

Download **`deepcool-lite-setup-<version>.exe`** below, run it and follow the steps. It is for the **DeepCool AK400 DIGITAL SE** cooler display (USB `3633:0001`) on Windows 10/11 (64-bit). Nothing else needs to be installed first: the setup contains everything, and asks before it installs the small PawnIO driver that is needed to show the CPU temperature.

Quit the official DeepCool software first: two programs must not write to the cooler.

**Windows SmartScreen:** the installer is not code-signed, so Windows may show "Windows protected your PC". Choose **More info**, then **Run anyway**. To check the file first, compare `Get-FileHash .\deepcool-lite-setup-<version>.exe` (PowerShell) with `SHA256SUMS.txt`.

**No warranty:** this is free software, provided as is. You use it at your own risk (see the MIT License).

How to use it, and what to do if the display stays blank, is in the README.
