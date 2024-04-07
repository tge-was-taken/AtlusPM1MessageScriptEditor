using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using AtlusScriptLibrary.Common.IO;
using AtlusScriptLibrary.Common.Libraries;
using AtlusScriptLibrary.Common.Logging;
using AtlusScriptLibrary.Common.Text.Encodings;
using AtlusScriptLibrary.MessageScriptLanguage;
using AtlusScriptLibrary.MessageScriptLanguage.Compiler;
using AtlusScriptLibrary.MessageScriptLanguage.Decompiler;


namespace AtlusPM1MessageScriptEditor
{
    internal static class Program
    {
        private static Assembly CurrentAssembly = Assembly.GetExecutingAssembly();
        private static AssemblyName AssemblyName = CurrentAssembly.GetName();
        private static Version Version = AssemblyName.Version;

        private static void Main( string[] args )
        {
            if ( args.Length == 0 )
            {
                Console.WriteLine( $"AtlusPM1MessageScriptEditor {Version.Major}.{Version.Minor} by TGE" );
                Console.WriteLine(  );
                Console.WriteLine( "Usage:" );
                Console.WriteLine($"    AtlusPM1MessageScriptEditor <path to file> [game]" );
                Console.WriteLine(  );
                Console.WriteLine( "Note:" );
                Console.WriteLine( "    You only need to specify the game if you are compiling with aliased function tags or you're decompiling scripts with japanese text" );
                Console.WriteLine( "    Game can either: 'p3' or 'p4'" );
                Console.WriteLine(  );
                Console.ReadKey();
                return;
            }

            var path = args[ 0 ];
            if ( !File.Exists( path ) )
            {
                Console.WriteLine( $"Specified file doesn't exist: {Path.GetFullPath( path )}" );
                return;
            }

            var game = args.Length > 1 ? args[ 1 ].ToLowerInvariant() : null;
            Encoding encoding = null;
            if ( game == "p3" )
                encoding = new Persona3Encoding();
            else if ( game == "p4" )
                encoding = new Persona4Encoding();

            if ( path.EndsWith( "pm1", StringComparison.InvariantCultureIgnoreCase ) )
            {
                // Extract message script from PM1
                var bytes = ExtractMessageScriptFromPM1( path );
                if ( bytes != null && bytes.Length > 0 )
                {
                    using ( var decompiler = new MessageScriptDecompiler( File.CreateText( Path.ChangeExtension( path, "msg" ) ) ) )
                    {
                        decompiler.Decompile( MessageScript.FromStream( new MemoryStream( bytes ) ) );
                    }
                }
                else
                {
                    Console.WriteLine( "No Message script present in PM1" );
                }
            }
            else if ( path.EndsWith( "msg", StringComparison.InvariantCultureIgnoreCase ) )
            {
                var pm1Path = Path.ChangeExtension( path, "pm1" );
                if ( !File.Exists( pm1Path ) )
                {
                    Console.WriteLine( $"{pm1Path} doesn't exist" );
                    return;
                }

                // Inject message script into PM1
                using ( var msgStream = File.OpenRead( path ) )
                {
                    var compiler = new MessageScriptCompiler( FormatVersion.Version1, encoding );
                    compiler.AddListener( new ConsoleLogListener( true, LogLevel.Info | LogLevel.Warning | LogLevel.Error | LogLevel.Fatal ) );

                    if ( game != null )
                        compiler.Library = LibraryLookup.GetLibrary( game );

                    if ( !compiler.TryCompile( msgStream, out var script ) )
                    {
                        Console.WriteLine( "Message script failed to compile" );
                        return;
                    }
                    else
                    {
                        Console.WriteLine( "Message script compilation completed successfully" );
                    }

                    using ( var stream = script.ToStream() )
                        InjectMessageScriptIntoPM1( pm1Path, pm1Path, ( ( MemoryStream ) stream ).ToArray() );

                    Console.WriteLine( $"{pm1Path} was patched successfully" );
                }
            }
            else
            {
                Console.WriteLine( "Invalid path specified. Expected pm1 or msg file." );
            }
        }

        private static byte[] ExtractMessageScriptFromPM1( string path )
        {
            using ( var reader = new BinaryReader( File.OpenRead( path ) ) )
            {
                reader.BaseStream.Seek( 0x10, SeekOrigin.Begin );
                int sectionCount = reader.ReadInt32();

                reader.BaseStream.Seek( 0x20, SeekOrigin.Begin );
                for ( int i = 0; i < sectionCount; i++ )
                {
                    int type = reader.ReadInt32();
                    int size = reader.ReadInt32();
                    int count = reader.ReadInt32();
                    int offset = reader.ReadInt32();

                    if ( type == 6 )
                    {
                        Trace.Assert( count == 1, "PM1 contains more than 1 message script" );
                        reader.BaseStream.Seek( offset, SeekOrigin.Begin );
                        return reader.ReadBytes( size );
                    }
                }
            }

            return null;
        }

        private static void InjectMessageScriptIntoPM1( string path, string outPath, byte[] data )
        {
            long sectionEntryOffset = 0;
            int size = 0;
            int offset = 0;
            using ( var reader = new BinaryReader( File.OpenRead( path ) ) )
            {
                reader.BaseStream.Seek( 0x10, SeekOrigin.Begin );
                int sectionCount = reader.ReadInt32();

                reader.BaseStream.Seek( 0x20, SeekOrigin.Begin );
                for ( int i = 0; i < sectionCount; i++ )
                {
                    sectionEntryOffset = reader.BaseStream.Position;
                    int type = reader.ReadInt32();
                    size = reader.ReadInt32();
                    int count = reader.ReadInt32();
                    offset = reader.ReadInt32();

                    if ( type == 6 )
                    {
                        Trace.Assert( count == 1, "PM1 contains more than 1 message script" );
                        break;
                    }
                }
            }

            using ( var writer = new BinaryWriter( File.OpenWrite( outPath ) ) )
            {
                writer.BaseStream.Seek( sectionEntryOffset + 4, SeekOrigin.Begin );
                writer.Write( ( int )data.Length );
                int sizeDifference = size - data.Length;

                if ( sizeDifference < 0 )
                {
                    // Append
                    var alignedOffset = AlignmentHelper.Align( writer.BaseStream.Length, 16 );
                    var alignedDifference = alignedOffset - writer.BaseStream.Length;

                    writer.BaseStream.Seek( sectionEntryOffset + 12, SeekOrigin.Begin );
                    writer.Write( ( int )alignedOffset );

                    writer.BaseStream.Seek( 0, SeekOrigin.End );
                    for ( int i = 0; i < alignedDifference; i++ )
                        writer.Write( ( byte )0 );

                    writer.Write( data );
                }
                else
                {
                    // Overwrite
                    writer.BaseStream.Seek( offset, SeekOrigin.Begin );
                    writer.Write( data );
                    for ( int i = 0; i < sizeDifference; i++ )
                        writer.Write( ( byte )0 );
                }
            }
        }
    }
}
