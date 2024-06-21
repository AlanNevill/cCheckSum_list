# cCheckSum_list


## <!--Functionality moved to GoogleTakeOut solution-->
CLI application that r

### Root command

Reads JPG image files from a root folder and its sub folders. Then inserts or truncate and inserts into a database table - `POPS.dbo.CheckSum`

##### Option --LogInvalid

Boolean option (default false), can be omitted. If true log files with invalid EXIF datetime.



### Load command



#### 	Option --folder

Must exist.
Example:  `--folder C:\Users\User\OneDrive\Photos\2005`

#### 	Option --replace
Default (false) append to table or (true) to truncate then insert into the db table CheckSum. Can be omitted.
`--replace true`



### Log file

Writes to console and log file in Logs folder.

### Example usage

Using PowerShell from Bin folder.

Truncate then insert into the CheckSum table.

`./cCheckSum_list Load --folder C:\\Users\\User\\OneDrive\\Photos --replace true`

Just append to the CheckSum table

`./cCheckSum_list Load --folder C:\Users\User\OneDrive\Photos`

Just append to the CheckSum table and write files with invalid EXIF datetime into the log.

`./cCheckSum_list Load --LogInvalid true --folder C:\Users\User\OneDrive\Photos`





