﻿using Dapper;

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
using System.Threading.Tasks;

namespace cCheckSum_list;

class Program
{
    private const string _connectionString = @"data source=SNOWBALL;initial catalog=pops;integrated security=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
    private static Stopwatch _stopWatch = new();
    private static int _invalidCount = 0;
    private static bool _logInvalid;
    private static int _count = 0;
    private static List<CheckSum> list2Insert = [];



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

        Log.Information( $"cCheckSum_list starting\n\tLogInvalid: {LogInvalid}\n\tRoot folder: {folder.FullName}\n\tTruncate CheckSum: {replace}" );

        // if --replace is true then truncate the CheckSum table
        if ( replace )
        {
            using IDbConnection db = new SqlConnection( _connectionString );
            db.Execute( "delete from dbo.CheckSum" );
        }

        // get an array of FileInfo objects from the folder tree
        FileInfo[] _files = folder.GetFiles( "*.JPG", SearchOption.AllDirectories );
        Log.Information( $"ProcessJPGs - Found {_files.Length:N0} *.JPG files to process under folder {folder.FullName}." );

        using ( SerilogTimings.Operation.Time( "ProcessJPGs - Using Parallel.ForEach( _files, ProcessJPG )" ) )
        {
            Log.Information( "Using Parallel.ForEach( _files, ProcessJPG )" );
            Parallel.ForEach( _files, ProcessJPG );
        }

        //using ( SerilogTimings.Operation.Time( "ProcessJPGs - Using AsParallel().ForAll" ) )
        //{
        //    Log.Information( "Using AsParallel().ForAll" );
        //    _files.AsParallel().ForAll( ProcessJPG );
        //}

        //Log.Information( "Using AsParallel().ForAll" );
        //_files.AsParallel().ForAll( ProcessJPG );

        //foreach ( FileInfo fi in _files )
        //{
        //    Log.Information( $"ProcessJPGs - fi.FullName: {fi.FullName}" );

        //    CheckSum checkSum = new()
        //    {
        //        Folder = fi.DirectoryName,
        //        TheFileName = fi.Name,
        //        FileExt = fi.Extension,
        //        FileSize = (int)fi.Length,
        //        FileCreateDt = fi.CreationTime,
        //    };

        //    IEnumerable<MetadataExtractor.Directory> directories = MetadataExtractor.ImageMetadataReader.ReadMetadata( fi.FullName );
        //    if ( directories == null )
        //    {
        //        Log.Warning( $"ProcessJPGs - MetadataExtractor.ImageMetadataReader.ReadMetadata returned null for {fi.FullName}" );
        //        continue;
        //    }

        //    var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        //    if ( subIfdDirectory == null )
        //    {
        //        Log.Warning( $"ProcessJPGs - subIfdDirectory is null, {fi.FullName}" );
        //        continue;
        //    }

        //    //foreach ( var directory in directories )
        //    //    foreach ( var tag in directory.Tags )
        //    //Log.Information( $"{directory.Name} - {tag.Name} = {tag.Description}"
        //    //
        //    var dateTime = subIfdDirectory?.GetDescription( ExifDirectoryBase.TagDateTimeOriginal );

        //    var result = (DateTime.TryParseExact( dateTime, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
        //                             DateTimeStyles.None, out DateTime dateValue ));

        //    if ( result )
        //    {
        //        checkSum.CreateDateTime = dateValue;
        //    }
        //    else
        //    {
        //        Log.Warning( $"ProcessJPGs - dateTime is not a date: [{dateTime}]" );
        //    }

        //    _count++;
        //    if ( _count % 1000 == 0 )
        //    {
        //        Log.Information( $"ProcessJPGs - {_count:N0}. Completed: {((_count * 100) / _files.Length)}%. Now processing folder: {fi.DirectoryName}" );
        //    }

        //    checkSum.SHA = CalculateSha256Checksum( fi );

        //    // add the CheckSum object to the list2Insert
        //    list2Insert.Add( checkSum );
        //} // end of foreach

        Log.Information( $"ProcessJPGs - Finished reading {_count:N0} files. {_invalidCount:N0} did not have valid DateTimeDigitized nor DateTime EXIF tags." );

        // insert the list of CheckSums (list2Insert) into the DB table POPS.CheckSum
        CheckSum_ins2( list2Insert );

        _stopWatch.Stop();
        Log.Information( $"ProcessJPGs - Processing complete in {_stopWatch.Elapsed.TotalSeconds} secs" );
    }

    private static void ProcessJPG(FileInfo fi)
    {
        //Log.Information( $"ProcessJPG - fi.FullName: {fi.FullName}" );

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
            return;
        }

        var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        if ( subIfdDirectory == null )
        {
            Log.Warning( $"ProcessJPGs - subIfdDirectory is null, {fi.FullName}" );
            return;
        }

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
            _invalidCount++;
            Log.Warning( $"ProcessJPG - dateTime is not a date: [{dateTime}]" );
        }

        _count++;
        if ( _count % 1000 == 0 )
        {
            Log.Information( $"ProcessJPG - {_count:N0}. Completed:. Now processing folder: {fi.DirectoryName}" );
        }

        checkSum.SHA = CalculateSha256Checksum( fi );

        // add the CheckSum object to the list2Insert
        list2Insert.Add( checkSum );
    }


    /// <summary>
    /// Insert the list of CheckSum rows into the database table.
    /// </summary>
    /// <param name="list2Insert">A list of CheckSum rows</param>
    private static void CheckSum_ins2(List<CheckSum> list2Insert)
    {
        Log.Information( $"CheckSum_ins2 - Starting insert of {list2Insert.Count:N0} CheckSum rows." );
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
        Log.Information( $"CheckSum_ins2 - Finished insert of {list2Insert.Count:N0} CheckSum rows in {insertStopWatch.Elapsed.TotalSeconds} secs." );
    }

    public static string CalculateSha256Checksum(FileInfo fileInfo)
    {
        // calculate the SHA256 checkSum for the file and return it with the elapsed processing time using a tuple
        FileStream fs = fileInfo.OpenRead();

        // ComputeHash - returns byte array  
        byte[] bytes = SHA256.Create().ComputeHash( fs );

        // BitConverter used to put all bytes into one string, hyphen delimited  
        return BitConverter.ToString( bytes );
    }

}
