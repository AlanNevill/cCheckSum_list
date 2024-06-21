using Dapper;

using MetadataExtractor.Formats.Exif;

//using ExifLib;

//using MetadataExtractor.IO;
//using MetadataExtractor.Formats.FileSystem;

using Microsoft.Data.SqlClient;

using Serilog;

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace cCheckSum_list;

class Program
{
    private const string _connectionString = @"data source=SNOWBALL;initial catalog=pops;integrated security=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
    private static Stopwatch _stopWatch = new();
    private static int _invalidCount = 0;
    private static bool _logInvalid;

    /// <summary>
    /// Application entry point
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    private static void Main(string[] args)
    {
        // SeriSerilog.Log.Information setup
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File( ".Logs/cCheckSum-.log", rollingInterval: RollingInterval.Day )
            .CreateLogger();


        RootCommand rootCommand = new( "Contains 1 commands." )
        {
            Name = "cCheckSum_list"
        };
        var logInvalid = new Option<bool>( "--LogInvalid", "Log files with invalid EXIF datetime", arity: ArgumentArity.ZeroOrOne );
        rootCommand.AddOption( logInvalid );

        // sub command to extract EXIF date/time from all JPG image files in a folder tree
        var ProcessJPGs = new Command( "Load", "Load EXIF data from all the JPG image files in a folder and its sub folders into CHECKSUM table." )
        {
            new Option<DirectoryInfo>("--folder", "The root folder to scan image files, e.g. 'C:\\Users\\User\\OneDrive\\Photos").ExistingOnly(),
            new Option<bool>("--replace", "Default (false) append to table or (true) to truncate then insert into the db table CheckSum.", arity: ArgumentArity.ZeroOrOne)
        };
        ProcessJPGs.Handler = CommandHandler.Create( (bool LogInvalid, DirectoryInfo folder, bool replace) => { Program.ProcessJPGs( LogInvalid, folder, replace ); } );

        // add the command ProcessJPGs to the rootCommand
        rootCommand.Add( ProcessJPGs );

        rootCommand.Invoke( args );
        //return await rootCommand.InvokeAsync( args );
    }

    /// <summary>
    /// Command1 EXIF - Process all the JPG images in a root folder and its sub folders
    /// </summary>
    /// <param name="LogInvalid">If true then JPG files with invalid EXIF datetime are logged.</param>
    /// <param name="folder">The root folder.</param>
    /// <param name="replace">Replace if true else append to CheckSum table.</param>
    private static void ProcessJPGs(bool LogInvalid, DirectoryInfo folder, bool replace)
    {
        _stopWatch.Start();
        _logInvalid = LogInvalid;
        int _count = 0;
        List<CheckSum> list2Insert = [];

        Log.Information( $"cCheckSum_list starting\n\tLogInvalid: {LogInvalid}\n\tRoot folder: {folder.FullName}\n\tTruncate CheckSum: {replace}" );

        // if --replace is true then truncate the CheckSum table
        if ( replace )
        {
            using IDbConnection db = new SqlConnection( _connectionString );
            db.Execute( "truncate table dbo.CheckSum" );
        }

        // get an array of FileInfo objects from the folder tree
        FileInfo[] _files = folder.GetFiles( "*.JPG", SearchOption.AllDirectories );
        Log.Information( $"Found {_files.Length:N0} *.JPG files to process under folder {folder.FullName}." );

        foreach ( FileInfo fi in _files )
        {
            CheckSum checkSum = new()
            {
                Folder = fi.DirectoryName,
                TheFileName = fi.Name,
                FileExt = fi.Extension,
                FileSize = (int)fi.Length,
                FileCreateDt = fi.CreationTime,
            };

            IEnumerable<MetadataExtractor.Directory> directories = MetadataExtractor.ImageMetadataReader.ReadMetadata( fi.FullName );
            if ( directories == null )
            {
                Log.Warning( $"ProcessJPGs - MetadataExtractor.ImageMetadataReader.ReadMetadata returned null for {fi.FullName}" );
                continue;
            }

            var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if ( subIfdDirectory == null )
            {
                Log.Warning( $"ProcessJPGs - subIfdDirectory is null, {fi.FullName}" );
                continue;
            }

            Log.Information( $"ProcessJPGs - fi.FullName: {fi.FullName}" );
            //foreach ( var directory in directories )
            //    foreach ( var tag in directory.Tags )
            //Log.Information( $"{directory.Name} - {tag.Name} = {tag.Description}"
            //
            var dateTime = subIfdDirectory?.GetDescription( ExifDirectoryBase.TagDateTimeOriginal );

            var result = (DateTime.TryParseExact( dateTime, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
                                     DateTimeStyles.None, out DateTime dateValue ));

            if ( result )
            {
                checkSum.CreateDateTime = dateValue;
            }
            else
            {
                Log.Warning( $"ProcessJPGs - dateTime is not a date: {dateTime}" );
            }

            _count++;
            if ( _count % 1000 == 0 )
            {
                Log.Information( $"{_count:N0}. Completed: {((_count * 100) / _files.Length)}%. Now processing folder: {fi.DirectoryName}" );
            }

            checkSum.SHA = calcShaHash2( fi );

            // add the CheckSum object to the list2Insert
            list2Insert.Add( checkSum );
        } // end of foreach


        //foreach ( FileInfo fi in _files )
        //{
        //    // get the EXIF DateTime or DateTimeDigitized from the JPG file
        //    (DateTime? _CreateDateTime, string _sCreateDateTime) = ImageEXIF( fi );

        //    // instantiate a new CheckSum object for the file
        //    var checkSum = new CheckSum
        //    {
        //        SHA = calcShaHash2(fi),
        //        Folder = fi.DirectoryName,
        //        TheFileName = fi.Name,
        //        FileExt = fi.Extension,
        //        FileSize = (int)fi.Length,
        //        FileCreateDt = fi.CreationTime,
        //        CreateDateTime = _CreateDateTime,
        //        SCreateDateTime = _sCreateDateTime
        //    };

        //    list2Insert.Add( checkSum );

        //    _count++;
        //    if ( _count % 1000 == 0 )
        //    {
        //        Log.Information( $"{_count:N0}. Completed: {((_count * 100) / _files.Length)}%. Now processing folder: {fi.DirectoryName}" );
        //    }
        //}
        Log.Information( $"Finished reading {_count:N0} files. {_invalidCount:N0} did not have valid DateTimeDigitized nor DateTime EXIF tags." );

        // insert the list of CheckSums (list2Insert) into the DB table POPS.CheckSum
        CheckSum_ins2( list2Insert );

        _stopWatch.Stop();
        Log.Information( $"Processing complete in {_stopWatch.Elapsed.TotalSeconds} secs" );
    }



    /// <summary>
    /// Extract EXIF DateTimeDigitized or DateTime from the JPG file passed in
    /// </summary>
    /// <param name="fileInfo">A FileInfo object JPG file</param>
    /// <returns>A tuple: DateTime CreateDateTime, string sCreateDateTime</returns>
    private static (DateTime? CreateDateTime, string sCreateDateTime) ImageEXIF(FileInfo fileInfo)
    {

        DateTime? _createDateTime = null;
        DateTime? _CreateDateTime = null;

        //ExifReader reader;

        // create a EXIF reader. If no EXIF data then return CreateDateTime: null & sCreateDateTime: "Date not found"
        //try
        //{
        //    reader = new( fileInfo.FullName );
        //}
        //catch ( ExifLibException elex ) when ( elex.Message.Contains( "File is not a valid JPEG" ) )
        //{
        //    if ( _logInvalid ) { Log.Error( $"File is not a valid JPEG: {fileInfo.FullName}" ); }

        //    _invalidCount++;
        //    return (CreateDateTime: _createDateTime, sCreateDateTime: "File not valid JPEG");
        //}
        //catch ( ExifLibException elex ) when ( elex.Message.Contains( "Error indexing EXIF tags" ) )
        //{
        //    if ( _logInvalid ) { Log.Error( $"Error indexing EXIF tag: {fileInfo.FullName}" ); }

        //    _invalidCount++;
        //    return (CreateDateTime: _createDateTime, sCreateDateTime: "Error indexing EXIF tag");
        //}
        //catch ( Exception exc )
        //{
        //    if ( exc.Message.Contains( "Unable to locate EXIF content" ) )
        //    {
        //        if ( _logInvalid ) { Log.Error( $"Unable to locate EXIF content in : {fileInfo.FullName}" ); }

        //        _invalidCount++;
        //        return (CreateDateTime: _createDateTime, sCreateDateTime: "Date not found");
        //    }
        //    else
        //    {
        //        throw;
        //    }
        //}

        // EXIF data found. Now try to extract DateTimeDigitized or DateTime ExifTags
        //if ( !reader.GetTagValue<DateTime>( ExifTags.DateTimeDigitized, out _CreateDateTime ) )
        //{
        //    if ( !reader.GetTagValue<DateTime>( ExifTags.DateTime, out _CreateDateTime ) )
        //    {
        //        // no DateTimeDigitized or DateTime ExifTags found so return CreateDateTime: null & sCreateDateTime: "Date not found"
        //        if ( _logInvalid ) { Log.Error( $"ExifTags.DateTime not found in : {fileInfo.FullName}" ); }
        //        _invalidCount++;
        //        return (CreateDateTime: _createDateTime, sCreateDateTime: "Date not found");
        //    }
        //}

        // return a valid date and string date
        return (CreateDateTime: _CreateDateTime, sCreateDateTime: _CreateDateTime.ToString());
    }


    /// <summary>
    /// Insert the list of CheckSum rows into the database table.
    /// </summary>
    /// <param name="list2Insert">A list of CheckSum rows</param>
    private static void CheckSum_ins2(List<CheckSum> list2Insert)
    {
        Log.Information( $"Starting insert of {list2Insert.Count:N0} CheckSum rows." );
        Stopwatch insertStopWatch = new();
        insertStopWatch.Start();
        IDbConnection db = new SqlConnection( _connectionString );

        foreach ( var checkSum in list2Insert )
        {
            // create the SqlParameters for the stored procedure using Dapper dynamic parameters
            var p = new DynamicParameters();
            p.Add( "@SHA", checkSum.SHA );
            p.Add( "@Folder", checkSum.Folder );
            p.Add( "@TheFileName", checkSum.TheFileName );
            p.Add( "@FileExt", checkSum.FileExt );
            p.Add( "@FileSize", checkSum.FileSize );
            p.Add( "@FileCreateDt", checkSum.FileCreateDt );
            p.Add( "@TimerMs", checkSum.TimerMs );
            p.Add( "@Notes", "" );
            p.Add( "@CreateDateTime", checkSum.CreateDateTime ?? (DateTime?)null );   // check for null

            // call the stored procedure dbo.spCheckSum_ins2
            db.Execute( "dbo.spCheckSum_ins2", p, commandType: CommandType.StoredProcedure );
        }

        insertStopWatch.Stop();
        Log.Information( $"Finished insert of {list2Insert.Count:N0} CheckSum rows in {insertStopWatch.Elapsed.TotalSeconds} secs." );
    }

    public static string calcShaHash2(FileInfo fileInfo)
    {
        // calculate the SHA256 checkSum for the file and return it with the elapsed processing time using a tuple
        FileStream fs = fileInfo.OpenRead();

        // ComputeHash - returns byte array  
        byte[] bytes = SHA256.Create().ComputeHash( fs );

        // BitConverter used to put all bytes into one string, hyphen delimited  
        return BitConverter.ToString( bytes );
    }

}
