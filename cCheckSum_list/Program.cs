using System;
using Serilog;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace cCheckSum_list
{
    class Program
    {
        private static async Task<int> Main(string[] args)
        {
            // Serilog setup
            Serilog.Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("./Logs/cCheckSum-.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Serilog.Log.Information("cCheckSum_list starting\n");

            RootCommand rootCommand = new("Contains 1 commands.");
            rootCommand.Name = "Transtest";

            // sub command to extract EXIF date/time from all JPG image files in a folder tree
            #region "subcommand EXIF"
            var command2 = new Command("EXIF", "Load EXIF data from all the JPG image files in a folder and its sub folders into CHECKSUM table.")
            {
                new Option<DirectoryInfo>("--folder", "The root folder to scan image files, e.g. 'C:\\Users\\User\\OneDrive\\Photos").ExistingOnly(),
                new Option<bool>("--replace", "Replace default (true) or append (false) to the db tables CheckSum.", arity: ArgumentArity.ZeroOrOne)
            };
            command2.Handler = CommandHandler.Create((DirectoryInfo folder, bool replace) => { ProcessEXIF(folder, replace); });
            rootCommand.AddCommand(command2);
            #endregion

            // add the command2 to the rootCommand
            rootCommand.Add(command2);

            return await rootCommand.InvokeAsync(args);

        }

        private static void ProcessEXIF(DirectoryInfo folder, bool replace)
        {
            int _count = 0;
            string mess = $"\r\n{DateTime.Now}, INFO - ProcessEXIF: target folder is {folder.FullName}\n\rTruncate CheckSum is: {replace}.";
            Log(mess);
            Console.WriteLine($"{DateTime.Now}, INFO - press any key to start processing.");
            Console.ReadLine();

            if (replace)
            {
                using IDbConnection db = new SqlConnection(_connectionString);
                db.Execute("truncate table dbo.CheckSum");
            }

            // get an array of FileInfo objects from the folder tree
            FileInfo[] _files = folder.GetFiles("*.JPG", SearchOption.AllDirectories);

            foreach (FileInfo fi in _files)
            {
                // get the EXIF date/time 
                (DateTime _CreateDateTime, string _sCreateDateTime) = ImageEXIF(fi);


                // instantiate a new CheckSum object for the file
                var checkSum = new CheckSum
                {
                    SHA = "",
                    Folder = fi.DirectoryName,
                    TheFileName = fi.Name,
                    FileExt = fi.Extension,
                    FileSize = (int)fi.Length,
                    FileCreateDt = fi.CreationTime,
                    CreateDateTime = _CreateDateTime,
                    SCreateDateTime = _sCreateDateTime
                };

                // insert into DB table
                CheckSum_ins2(checkSum);


                _count++;

                if (_count % 1000 == 0)
                {
                    mess = $"{DateTime.Now}, INFO - {_count:N0}. Completed: {((_count * 100) / _files.Length)}%. Processing folder: {fi.DirectoryName}";
                    Log(mess);
                }
            }

        }


        private static void ProcessEXIF(DirectoryInfo folder, bool replace)
        {
            throw new NotImplementedException();
        }

    }
}
