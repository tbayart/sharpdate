# XML Syntax #

Here is an example of how the XML file should look like:
```
<xml>
  <program>
    <name>sharpdate</name>
    <version>1.0.0.0</version>
    <beta>0</beta>
    <changelog>
    Changelog v1.0

    Fixed y
    Removed z
    Added x
    </changelog>
    <downloadURL>http://sharpdate.googlecode.com/files/</downloadURL>
  </program>
</xml>
```

If we take a look at the downloadURL, it should have a trailing slash.
Sharpdate would download this update from this url:
http://sharpdate.googlecode.com/files/1.0.0.0.exe

Which should be the correct path to an installer. Sharpdate is currently limited to downloading one executable, which will then be run when downloaded. Basically an installer which should be configured to detect the already present installation and so on. More flexible functionality will be added in the future.

# SharpDate syntax #

```
SharpDate.exe [options]
```

To look for an update you run sharpdate with some specific arguments.
Here is all that are available at the moment:


```
-apiurls [api urls separated by | ]
```

This should be one or more urls to an xml file that contains the update data. It can also be a url to a php that generates an xml. If the first url doesn't work (no connection etc.) it will try the next and so on.


```
-pids [PIDs separated by comma]
```

The process IDs of all processes you want sharpdate to send exit to before updating.


```
-version [version, ex. 1.2.2.0]
```

The installed version of the program you are updating.


```
-mainexe [path to the main.exe, relative to sharpdate.exe]
```

If -version hasn't been specified, SharpDate will pull the fileversion from this exe. If -pids hasn't been specified, SharpDate will kill all the processes with this exe-name instead.