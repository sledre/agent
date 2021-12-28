# SledRE Agent

This is the agent running on windows sandbox where the malware are executed.  
It is running in a Service as LocalSystem on the Windows 7 VM.  
The agent is linked to the workers API so it can register itself to SledRE, find a task and submit the results.

You should refer to the main repository [SledRE](https://github.com/sledre/sledre) for any information.

## Contributing
### Build

* Load the project to Visual Studio
* Build the solution

### Install

* Open VS Console as Administrator
* Run the following command ```InstallUtil SledREAgent.exe```

### Uninstall

* Open VS Console as Administrator
* Run the following command ```InstallUtil /u SledREAgent.exe```
