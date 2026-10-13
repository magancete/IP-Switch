# Security policy

## Supported versions

Only the [latest release](https://github.com/magancete/IP-Switch/releases/latest) receives security fixes.

## Reporting a vulnerability

Please **do not open a public issue**. Contact the maintainer privately through [carlosmagan.com](https://carlosmagan.com)
(or the *Report a vulnerability* button in the repository's **Security** tab, when available).

Include the IP Switch version, the Windows version, the steps to reproduce and the impact. Once a fix is released,
the report will be credited unless you prefer otherwise.

## Scope

IP Switch runs with administrator rights because it changes the network configuration with `netsh`. Issues such as
command injection through profile data, adapter names or imported files, or a way to run code with those rights, are
in scope.
