using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
using log4net;


namespace FTPClient
{
    public class FtpService
    {
        #region Variables
        //Property Variables
        private string _ftpServer = string.Empty;
        private string _ftpPath = ".";
        private string _ftpUsername = string.Empty;
        private string _ftpPassword = string.Empty;
        private int _ftpPort = 21;
        private bool _isBinary = false;
        private int _timeOut = 10;
        //Static variables
        private const int BufferSize = 512;
        private static readonly Encoding ASCII = Encoding.ASCII;
        //Misc Global variables
        private bool _doVerbose = false;
        private string _statusMessage = string.Empty;
        private string _result = string.Empty;
        private int bytes = 0;
        private readonly Byte[] buffer = new Byte[BufferSize];
        private Socket _ftpSocket = null;
        private ILog _log;
        #endregion

        #region Class Properties
        #region Read/Write Properties
        /// <summary>
        /// Display all communications to the debug log
        /// </summary>
        public bool DoVerbose
        {
            get { return _doVerbose; }
            set { _doVerbose = value; }
        }

        /// <summary>
        /// FTP Server port to use, default is usually 21
        /// </summary>
        public int FtpPort
        {
            get { return _ftpPort; }
            set { _ftpPort = value; }
        }

        /// <summary>
        /// Timeout waiting for a response from server, in seconds.
        /// </summary>
        public int TimeoutValue
        {
            get { return _timeOut; }
            set { _timeOut = value; }
        }

        /// <summary>
        /// Name of the FTP server we wish to connect to
        /// </summary>
        /// <returns></returns>
        public string FtpServer
        {
            get { return _ftpServer; }
            set { _ftpServer = value; }
        }

        /// <summary>
        /// The remote port we wish to connect through
        /// </summary>
        /// <returns></returns>
        public int RemotePort
        {
            get { return _ftpPort; }
            set { _ftpPort = value; }
        }

        /// <summary>
        /// The working directory
        /// </summary>
        public string FtpPath
        {
            get { return _ftpPath; }
            set { _ftpPath = value; }

        }

        /// <summary>
        /// Server username
        /// </summary>
        public string FtpUsername
        {
            get { return _ftpUsername; }
            set { _ftpUsername = value; }
        }

        /// <summary>
        /// Server password
        /// </summary>
        public string FtpPassword
        {
            get { return _ftpPassword; }
            set { _ftpPassword = value; }
        }



        /// <summary>
        /// If the value of mode is true, set 
        /// binary mode for downloads, else, Ascii mode.
        /// </summary>
        public bool IsBinary
        {
            get { return _isBinary; }
            set
            {
                // if _isBinary already exit
                if (_isBinary == value) return;
                
                // check the value being passed
                // if it's true send the command
                // for binary download
                Execute(value ? "TYPE I" : "TYPE A");
                
                // now check the status code, if
                // it's not 200 throw an exception
                if (StatusCode != 200)
                {
                    Log.Error(_result.Substring(4));
                    throw new FtpException(_result.Substring(4));
                }
            }
        }

        public ILog Log
        {
            get
            {
                if (_log == null)
                {
                    _log = Logger.Create();
                    return _log;
                }
                return _log;
            } 
            set { _log = value; }
        }
        #endregion

        public FtpService(string ftpServer, string ftpPath, string ftpUsername, string ftpPassword)
        {
            StatusCode = 0;
            IsLoggedIn = false;
            _ftpServer = ftpServer;
            _ftpPath = ftpPath;
            _ftpUsername = ftpUsername;
            _ftpPassword = ftpPassword;
        }

        #region ReadOnly Properties

        /// <summary>
        /// determine if the user is logged in
        /// </summary>
        public bool IsLoggedIn { get; private set; }

        /// <summary>
        /// returns the status code of the command
        /// </summary>
        public int StatusCode { get; private set; }

        #endregion
        #endregion

        #region FTPFtpLogin
        /// <summary>
        /// method to log in to the remote ftp server
        /// </summary>
        public void FtpLogin()
        {
            //check if the connection is currently open
            if (IsLoggedIn)
            {
                //its open so we need to close it
                CloseConnection();
            }
            //message that we're connection to the server
            Log.InfoFormat("Opening connection to {0}", _ftpServer);
            //create our ip address object
            //create our end point object
            IPEndPoint addrEndPoint = null;
            try
            {
                // create our ftp socket
                _ftpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                
                // retrieve the server ip
                // first validate if it's already an ip address
                IPAddress ipAddress;

                bool isValidIp = IPAddress.TryParse(_ftpServer, out ipAddress);

                IPAddress remoteAddress = !isValidIp ? Dns.GetHostEntry(_ftpServer).AddressList[0] : ipAddress;
                
                // set the endpoint value
                addrEndPoint = new IPEndPoint(remoteAddress, _ftpPort);
                // connect to the ftp server
                _ftpSocket.Connect(addrEndPoint);
            }
            catch (Exception ex)
            {
                // since an error happened, we need to
                //close the connection and throw an exception
                if (_ftpSocket != null && _ftpSocket.Connected)
                {
                    _ftpSocket.Close();
                }
                Log.ErrorFormat("Couldn't connect to remote server {0}", ex);
                throw new FtpException("Couldn't connect to remote server", ex);
            }
            //read the host response
            ReadResponse();
            //check for a status code of 220
            if (StatusCode != 220)
            {
                //failed so close the connection
                CloseConnection();
                Log.Error(_result.Substring(4));
                //throw an exception
                throw new FtpException(_result.Substring(4));
            }
            //execute the USER ftp command (sends the username)
            Execute("USER " + _ftpUsername);
            //check the returned status code
            if (!(StatusCode == 331 || StatusCode == 230))
            {
                //not what we were looking for so
                //logout and throw an exception
                LogOut();
                Log.Error(_result.Substring(4));
                throw new FtpException(_result.Substring(4));
            }
            //if the status code isnt 230
            if (StatusCode != 230)
            {
                //execute the PASS ftp command (sends the password)
                Execute("PASS " + _ftpPassword);
                //check the returned status code
                if (!(StatusCode == 230 || StatusCode == 202))
                {
                    //not what we were looking for so
                    //logout and throw an exception
                    LogOut();
                    Log.Error(_result.Substring(4));
                    throw new FtpException(_result.Substring(4));
                }
            }
            //we made it this far so we're logged in
            IsLoggedIn = true;
            //verbose the login message
            Log.InfoFormat("Connected to {0}", _ftpServer);
            //set the initial working directory
            ChangeWorkingDirectory(_ftpPath);
        }
        #endregion

        #region CloseConnection
        /// <summary>
        /// method to close the connection
        /// </summary>
        public void CloseConnection()
        {
            //display the closing message
            Log.InfoFormat("Closing connection to {0}",  _ftpServer);
            //check to see if the connection is still active
            //if it is then execute the ftp quit command
            //which terminates the connection
            if (_ftpSocket != null)
            {
                Execute("QUIT");
            }
            //log the user out
            LogOut();
        }
        #endregion

        #region Excute
        private void Execute(String msg)
        {
	        //check to see if verbose debugging is enabled
	        //if so write the command to the window
	        if (_doVerbose) Log.InfoFormat(msg,"FtpClient");
	        //convert the command to a byte array
	        Byte[] cmdBytes = Encoding.ASCII.GetBytes((msg + "\r\n").ToCharArray());
	        //send the command to the host
	        _ftpSocket.Send(cmdBytes, cmdBytes.Length, 0);
	        //read the returned response
	        ReadResponse();
        }
        #endregion

        #region readResponse
        /// <summary>
        /// 
        /// </summary>
        private void ReadResponse()
        {
	        _statusMessage = "";
	        _result = ParseHostResponse();
	        StatusCode = int.Parse(_result.Substring(0,3));
        }
        #endregion

        #region ParseHostResponse
        /// <summary>
        /// Method to parse the response from the remote host
        /// </summary>
        /// <returns></returns>
        private string ParseHostResponse()
        {
            while (true)
            {
                //retrieve the host response and convert it to
                //a byte array
                bytes = _ftpSocket.Receive(buffer, buffer.Length, 0);
                //decode the byte array and set the
                //statusMessage to its value
                _statusMessage += ASCII.GetString(buffer, 0, bytes);
                //check the size of the byte array
                if (bytes < buffer.Length)
                {
                    break;
                }
            }
            //split the host response
            string[] msg = _statusMessage.Split('\n');
            //check the length of the response
            if (_statusMessage.Length > 2)
                _statusMessage = msg[msg.Length - 2];
            else
                _statusMessage = msg[0];

            //check for a space in the host response, if it exists return
            //the message to the client
            if (!_statusMessage.Substring(3, 1).Equals(" ")) return ParseHostResponse();
            //check if the user selected verbose Debugging
            if (_doVerbose)
            {
                //loop through the message from the host
                for (int i = 0; i < msg.Length - 1; i++)
                {
                    //write each line out to log
                    Log.InfoFormat("{0}", msg[i]);
                }
            }
            //return the message
            return _statusMessage;
        }
        #endregion

        /// <summary>
        /// when doing data transfers, we need to open another socket for it.
        /// </summary>
        /// <returns>Connected socket</returns>
        private Socket OpenSocketForTransfer()
        {
            //send the PASV command (Passive command)
            Execute("PASV");
            //check the status code, if it
            //isnt 227 (successful) then throw an exception
            if (StatusCode != 227)
            {
                Log.Error(_result.Substring(4));
                throw new FtpException(_result.Substring(4));
            }
            //find the index of the opening "("
            //and the closing ")". The return
            //message from the server, if successful, has
            //the IP and port number for the client in
            //enclosed in "(" & ")"
            int idx1 = _result.IndexOf('(');
            int idx2 = _result.IndexOf(')');
            //now we need to get everything in the parenthesis
            string ipData = _result.Substring((idx1 + 1), (idx2 - idx1) - 1);
            //create new integer array with size of 6
            //the returning message is in 6 segments
            int[] msgSegments = new int[6];
            //get the length of the message
            int msgLength = ipData.Length;
            int partCount = 0;
            string s = "";
            //now we need to loop through the host response
            for (int i = 0; i < msgLength && partCount <= 6; i++)
            {
                //convert each character to a char
                char chr = char.Parse(ipData.Substring(i, 1));
                //check to see if the current character is numeric
                if (char.IsDigit(chr))
                {
                    //since its a number we add it to our buffer variable
                    s += chr;
                }
                //now we need to check for the
                //comma seperating the digits
                else if (chr != ',')
                {
                    Log.ErrorFormat("Malformed PASV result: {0}", _result);
                    //no comma so throw an exception
                    throw new FtpException("Malformed PASV result: " + _result);
                }
                else
                {
                    //check to see if the current character is a comma
                    //or if the counter + 1 equals the host response length
                    if (chr == ',' || i + 1 == msgLength)
                    {
                        try
                        {
                            //since its one of the 2 we add it to the
                            //current index of the message segments
                            msgSegments[partCount++] = int.Parse(s);
                            s = "";
                        }
                        //handle any exceptions thrown
                        catch (Exception ex)
                        {
                            Log.ErrorFormat("Malformed PASV result (not supported?): {0} - {1}", _result, ex);
                            throw;
                        }
                    }
                }
            }
            //now we assemble the IP address returned from the host
            string ipAddress = msgSegments[0] + "." + msgSegments[1] + "." + msgSegments[2] + "." + msgSegments[3];
            //the last 2 segments are the port we need to use
            int port = (msgSegments[4] << 8) + msgSegments[5];

            Socket tranferSocket = null;

            try
            {
                //create our new socket for transfering data
                tranferSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPEndPoint ipEndPoint = new IPEndPoint(Dns.GetHostEntry(ipAddress).AddressList[0], port);
                tranferSocket.Connect(ipEndPoint);
            }
            catch (Exception ex)
            {
                // doubtfull....
                if (tranferSocket != null && tranferSocket.Connected) tranferSocket.Close();
                Log.ErrorFormat("Can't connect to remote server {0}", ex);
                //throw an FtpException
                throw;
            }
            //return the socket
            return tranferSocket;
        }

        #region Upload
        /// <summary>
        /// Upload a file and set the resume flag.
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="resume"></param>
        public void Upload(string fileName, bool resume)
        {
            // make sure the user is logged in
            if (!IsLoggedIn)
            {
                //FtpLogin();
                Log.Error("You need to log in before you can perform this operation");
                throw new FtpException("You need to log in before you can perform this operation");
            }

            long resumeOffset = 0;
            
            // if resume is true
            if (resume)
            {
                try
                {
                    // set _isBinary to true
                    IsBinary = true;
                    
                    // get the size of the file
                    resumeOffset = GetFileSize(Path.GetFileName(fileName));
                }
                catch (Exception)
                {
                    // file not exist
                    resumeOffset = 0;
                }
            }

            // open stream to read file
            FileStream input = new FileStream(fileName, FileMode.Open);
            
            // if resume is true
            // and the size of the file read is
            // less than the initial value
            if (resume && input.Length < resumeOffset)
            {
                // different file size
                Log.InfoFormat("Overwriting {0}", fileName);
                resumeOffset = 0;
            }
            else if (resume && input.Length == resumeOffset)
            {
                // file done
                input.Close();
                Log.InfoFormat("Skipping completed {0} - turn resume off to not detect.", fileName);
                return;
            }

            // now create our socket needed for
            // the file transfer
            Socket dataSocket = OpenSocketForTransfer();
            
            // if the file size is greater than 0
            if (resumeOffset > 0)
            {
                // execute the rest command, which
                // sets the point the resume will occurr
                // if the upload is interrupted
                Execute("REST " + resumeOffset);
                
                // check the status code, if it's not
                // 350 the resume isnt supported by the server
                if (StatusCode != 350)
                {
                    Log.InfoFormat("Resuming not supported - status code {0}", StatusCode);
                    resumeOffset = 0;
                }
            }

            Log.InfoFormat("Attempting to upload file {0}", fileName);
            
            // execute the store ftp command (starts the transfer of the file)
            Execute("STOR " + Path.GetFileName(fileName));
            
            // check the status code, we need a
            // value of 150 or 125, otherwise throw an exception
            if (StatusCode != 125 && StatusCode != 150)
            {
                Log.ErrorFormat("Status Code {0} - {1}", StatusCode, _result.Substring(4));
                throw new FtpException(_result.Substring(4));
            }
            
            // now check the resumeOffset value,
            // if its not zero then we need to resume
            // the upload process where it ended
            if (resumeOffset != 0)
            {
                 
                // let the user know the upload is resuming
                Log.InfoFormat("Resuming at offset " + resumeOffset, "FtpClient");
                
                // use the Seek method to get to where the upload ended
                input.Seek(resumeOffset, SeekOrigin.Begin);
            }
            
            Log.InfoFormat("Uploading file {0} to {1}", fileName, _ftpPath);
            
            // upload the file
            while ((bytes = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                dataSocket.Send(buffer, bytes, 0);
            }
            
            // close our reader
            input.Close();
            
            // check to see if the socket is still connected
            // if it is then disconnect it
            if (dataSocket.Connected)
            {
                dataSocket.Close();
            }
            
            // read the host's response
            ReadResponse();
            
            // checking for a successful upload code (226 or 250)
            // if not either then throw an exception
            if (StatusCode != 226 && StatusCode != 250)
            {
                Log.ErrorFormat("Status Code {0} - {1}", StatusCode, _result.Substring(4));
                throw new FtpException(_result.Substring(4));
            }

            Log.InfoFormat("Uploaded file {0} to {1} successfully", fileName, _ftpPath);
        }
        #endregion


        #region GetFileSize
        /// <summary>
        /// Method to retrieve the size of the file based
        /// on the name provided
        /// </summary>
        /// <param name="file">Name of the file to get the size of</param>
        /// <returns>The files size</returns>
        public long GetFileSize(string file)
        {
            //make sure the user is logged in
            if (!IsLoggedIn)
            {
                Log.Error("You need to log in before you can perform this operation");
                //FtpLogin();
                throw new FtpException("You need to log in before you can perform this operation");
            }
            //execute the size command, which
            //returns the files size as a decimal number
            Execute("SIZE " + file);
            long fileSize;
            //check our returning status code
            //if it's not 213 the command failed
            if (StatusCode == 213)
            {
                //set the file size
                fileSize = long.Parse(_result.Substring(4));
            }
            else
            {
                Log.ErrorFormat("Status Code {0} - {1}", StatusCode, _result.Substring(4));
                //command failed so throw an exception
                throw new FtpException(_result.Substring(4));
            }
            //return the file size
            return fileSize;
        }
        #endregion

        #region Download
        /// <summary>
        /// Download a remote file to a local file name which can include
        /// a path, and set the resume flag. The local file name will be
        /// created or overwritten, but the path must exist.
        /// </summary>
        /// <param name="ftpFile">File on the server to download</param>
        /// <param name="localFile">Name of the file on the local machine</param>
        /// <param name="resume"></param>
        public void Download(string ftpFile, string localFile, Boolean resume)
        {
            // make sure the user is logged in
            if (!IsLoggedIn)
            {
                throw new FtpException("You need to log in before you can perform this operation");
            }

            IsBinary = true;
            
            // display a downloading file message
            Log.InfoFormat("Downloading file {0} from {1}/{2}", ftpFile, _ftpServer, _ftpPath);
            
            // check if a local file name was provided
            // if not then set its value to the ftp file name
            if (localFile.Equals(""))
            {
                localFile = ftpFile;
            }
            
            // create our filestream object
            FileStream output;
            
            // check to see if the local file exists
            // if it doesnt then create the file
            // otherwise overwrite it
            output = !File.Exists(localFile) ? File.Create(localFile) : new FileStream(localFile, FileMode.Open);

            // create our new socket for the transfer
            Socket dataSocket = OpenSocketForTransfer();
            
            // create our resume point

            // if resume was set to true
            if (resume)
            {
                // set the value of our resume variable
                long resumeOffset = output.Length;
                // check if its value is greater than 0 (zero)
                if (resumeOffset > 0)
                {
                    // execute our rest command, which sets the
                    // resume point for the download in case
                    // the download is interrupted
                    Execute("REST " + resumeOffset);
                    
                    // check the status code, if not a 350
                    // code then the server doesn't support resuming
                    if (StatusCode != 350)
                    {
                        // Server doesn't support resuming
                        Log.InfoFormat("Resuming not supported: {0}", _result.Substring(4));
                    }
                    else
                    {
                        Log.InfoFormat("Resuming at offset " + resumeOffset, "FtpClient");
                        // seek to the interrupted point
                        output.Seek(resumeOffset, SeekOrigin.Begin);
                    }
                }
            }

            Log.InfoFormat("Attempting to retrieve file {0}", ftpFile);
            // execute out retr command
            // which starts the file transfer
            Execute("RETR " + ftpFile);
            
            // check the status code, we need 150 or 125
            // otherwise the download failed
            if (StatusCode != 150 && StatusCode != 125)
            {
                Log.ErrorFormat("Status Code {0} - {1}", StatusCode, _result.Substring(4));
                // throw an FtpException
                throw new FtpException(_result.Substring(4));
            }
            
            // set a timeout value
            DateTime timeout = DateTime.Now.AddSeconds(_timeOut);
            
            // check the timeout value against the current time
            // if it's less then download the file
            while (timeout > DateTime.Now)
            {
                // receive the binary data from the socket
                bytes = dataSocket.Receive(buffer, buffer.Length, 0);
                // write the file
                output.Write(buffer, 0, bytes);
                
                // make sure the file is greater than
                // zero in size, if not exit the method
                if (bytes <= 0)
                {
                    break;
                }
            }
            
            // close our stream
            output.Close();
            // check to see if the socket is still open,
            // if it is then close it
            if (dataSocket.Connected)
            {
                dataSocket.Close();
            }
            //read the host's response
            ReadResponse();
            //we're looking for a status code of 226 or 250,
            //if that isnt returned the download failed
            if (StatusCode != 226 && StatusCode != 250)
            {
                Log.ErrorFormat("Status Code {0} - {1}", StatusCode, _result.Substring(4));
                throw new FtpException(_result.Substring(4));
            }
            Log.InfoFormat("Downloaded file {0} to {1} successfully", ftpFile, localFile);

        }
        #endregion

        #region DirectoryListSimple
        /// <summary>
        /// Return a string array containing the remote directory's file simple list.
        /// </summary>
        /// <param name="mask"></param>
        /// <returns></returns>
        public IEnumerable<string> DirectoryListSimple(string mask)
        {
            // make sure the user is logged in
            if (!IsLoggedIn)
            {
                //FtpLogin();
                Log.ErrorFormat("You need to log in before you can perform this operation");
                throw new FtpException("You need to log in before you can perform this operation");
            }
            
            // create new socket
            Socket dataSocket = OpenSocketForTransfer();
            
            // execute the ftp nlst command, which
            // returns a list of files on the remote server
            Execute("NLST " + mask);
            
            // check the return code, we're looking for
            // either 150 or 125, otherwise the command failed
            if (!(StatusCode == 150 || StatusCode == 125))
            {
                Log.ErrorFormat("Status Code {0} - {1}", StatusCode, _result.Substring(4));
                //failed, throw an exception
                throw new FtpException(_result.Substring(4));
            }
            
            // set the message to empty
            _statusMessage = "";
            
            // create a timeout value based on our timeout property
            DateTime timeout = DateTime.Now.AddSeconds(_timeOut);
            
            // loop while out timeout value is
            // greater than the current time
            while (timeout > DateTime.Now)
            {
                //retrieve the data from the host
                int receivedBytes = dataSocket.Receive(buffer, buffer.Length, 0);
                //convert it to Ascii format
                _statusMessage += ASCII.GetString(buffer, 0, receivedBytes);
                //exit the method is nothing is returned
                if (receivedBytes < buffer.Length) break;
            }
            
            // chuck the returned message into a string array
            var msg = _statusMessage.Replace("\r", "").Split('\n').ToList();
            
            // close the socket connection
            dataSocket.Close();
            
            // check the return message
            if (_statusMessage.IndexOf("No such file or directory", StringComparison.Ordinal) != -1)
                //return an empty message
                msg = new List<string>();
            //read the host's response
            ReadResponse();
            //if we didnt receive a status code of 226
            //then the process failed
            if (StatusCode != 226)
                //return an empty message
                msg = new List<string>();
            
            return msg;
        }
        #endregion

        #region UploadDirectory
        /// <summary>
        /// Upload a directory and its file contents
        /// </summary>
        /// <param name="dirPath">Path of the directory to upload</param>
        /// <param name="recursive">Whether to recurse sub directories</param>
        /// <param name="fileMask">Only upload files of the given mask(i.e;'*.*','*.jpg', ect..)</param>
        public void UploadDirectory(string dirPath, bool recursive, string fileMask)
        {
            //make sure the user is logged in
            if (!IsLoggedIn)
            {
                //FtpLogin();
                Log.Error("You need to log in before you can perform this operation");
                throw new FtpException("You need to log in before you can perform this operation");
            }
            string[] directories = dirPath.Replace("/", @"\").Split('\\');
            string rootDirectory = directories[directories.Length - 1];

            // make the root dir if it does not exist
            if (!DirectoryListSimple(rootDirectory).Any())
            {
                CreateDirectory(rootDirectory);
            }
            
            // make the new directory the working directory
            ChangeWorkingDirectory(rootDirectory);
            
            // loop through the files in the directory
            foreach (var file in Directory.GetFiles(dirPath, fileMask))
            {
                // upload each file
                Upload(file, true);
            }
            
            // check if recusrsive was specified
            if (recursive)
            {
                // since recursive is true, we loop through all the
                // directories in the directory provided
                foreach (var directory in Directory.GetDirectories(dirPath))
                {
                    // upload each directory
                    UploadDirectory(directory, recursive, fileMask);
                }
            }
            // change working directory back to root level
            ChangeWorkingDirectory("..");
        }
        #endregion

        #region ChangeWorkingDirectory
        /// <summary>
        /// Change the current working directory on the remote FTP server.
        /// </summary>
        /// <param name="dirName"></param>
        public void ChangeWorkingDirectory(string dirName)
        {
            // check to make sure a directory name was supplied
            if (dirName == null || dirName.Equals(".") || dirName.Length == 0)
            {
                // no directory was provided so throw an exception 
                //and break out of the method
                Log.Error("A directory name wasn't provided. Please provide one and try your request again.");
                throw new FtpException("A directory name wasn't provided. Please provide one and try your request again.");
            }
            //before we can change the directory we need
            //to make sure the user is logged in
            if (!IsLoggedIn)
            {
                //FtpLogin();
                Log.Error("You need to log in before you can perform this operation");
                throw new FtpException("You need to log in before you can perform this operation");
            }

            Log.InfoFormat("Attempting to change working directory {0}", dirName);
            //execute the CWD command = Change Working Directory
            Execute("CWD " + dirName);
            //check for a return status code of 250
            if (StatusCode != 250)
            {
                Log.ErrorFormat("Status Code {0} - {1}", StatusCode, _result.Substring(4));
                //operation failed, throw an exception
                throw new FtpException(_result.Substring(4));
            }

            Log.InfoFormat("Attempting to list working directory {0}", dirName);
            //execute the PWD command
            //Print Working Directory
            Execute("PWD");
            //check for a status code of 250
            if (StatusCode != 257)
            {
                Log.ErrorFormat("Status Code {0} - {1}", StatusCode, _result.Substring(4));
                //operation failed, throw an exception
                throw new FtpException(_result.Substring(4));
            }
            // we made it this far so retrieve the
            //directory from the host response
            _ftpPath = _statusMessage.Split('"')[1];

            Log.InfoFormat("Current directory is {0}", _ftpPath);
        }
        #endregion

        #region Delete
        /// <summary>
        /// method to delete a file from the FTP server.
        /// </summary>
        /// <param name="file">File to delete</param>
        public void Delete(string file)
        {
            //make sure the user is logged in
            if (!IsLoggedIn)
            {
                //FtpLogin();
                Log.Error("You need to log in before you can perform this operation");
                throw new FtpException("You need to log in before you can perform this operation");
            }
            Log.InfoFormat("Attempting to delete file {0}", file);
            //execute the delete command
            Execute("DELE " + file);
            //check for a status code of 250, if
            //not then throw an exception
            if (StatusCode != 250)
            {
                Log.ErrorFormat("Status Code {0} - {1}", StatusCode, _result.Substring(4));
                throw new FtpException(_result.Substring(4));
            }
            Log.InfoFormat("Deleted file {0}", file);
        }
        #endregion

        #region Rename
        /// <summary>
        /// Rename a file on the remote FTP server.
        /// </summary>
        /// <param name="oldName">File to rename</param>
        /// <param name="newName">New name of the file</param>
        /// <param name="replace">setting to false will throw exception if it exists</param>
        public void Rename(string oldName, string newName, bool replace = true)
        {
            // make sure the user is logged in
            if (!IsLoggedIn)
            {
                //FtpLogin();
                Log.Error("You need to log in before you can perform this operation");
                throw new FtpException("You need to log in before you can perform this operation");
            }

            Log.InfoFormat("Attempting to rename old file {0} to {1}", oldName, newName);
            
            // execute the rename from command
            Execute("RNFR " + oldName);
            
            // check for a status code of 350
            if (StatusCode != 350)
            {
                Log.ErrorFormat("Status Code {0} - {1}", StatusCode, _result.Substring(4));
                throw new FtpException(_result.Substring(4));
            }
            
            // if they didnt choose to replace the file, and a 
            // file with that name already exists then throw an exception
            if (!replace && DirectoryListSimple(newName).Any())
            {
                Log.Error("File already exists");
                throw new FtpException("File already exists");
            }
            
            // execute the rename to command
            Execute("RNTO " + newName);
            // check for a status code of 250, if
            // not then throw an exception
            if (StatusCode != 250)
            {
                Log.ErrorFormat("Status Code {0} - {1}", StatusCode, _result.Substring(4));
                throw new FtpException(_result.Substring(4));
            }
            // write the successful message out
            Log.InfoFormat("Renamed file {0} to {1}", oldName, newName);
        }
        #endregion

        #region CreateDirectory
        /// <summary>
        /// Create a directory on the remote FTP server.
        /// </summary>
        /// <param name="dirName">Name of the directory to create</param>
        public void CreateDirectory(string dirName)
        {
            //make sure the user is logged in
            if (!IsLoggedIn)
            {
                //FtpLogin();
                Log.Error("You need to log in before you can perform this operation");
                throw new FtpException("You need to log in before you can perform this operation");
            }
            //check to make sure a directory name was supplied
            if (dirName == null || dirName.Equals(".") || dirName.Length == 0)
            {
                //no directory was provided so throw an exception 
                //and break out of the method
                Log.Error("A directory name wasn't provided. Please provide one and try your request again.");
                throw new FtpException("A directory name wasn't provided. Please provide one and try your request again.");
            }

            Log.InfoFormat("Attempting to create directory {0}", dirName);
            //execute the make directory command
            Execute("MKD " + dirName);
            //check for a status code of 250 or 257
            if (StatusCode != 250 && StatusCode != 257)
            {
                //operation failed, throw an exception
                Log.ErrorFormat("Status Code {0} - {1}", StatusCode, _result.Substring(4));
                throw new FtpException(_result.Substring(4));
            }

            Log.InfoFormat("Created directory {0}", dirName);
        }
        #endregion

        /// <summary>
        /// method to release and remove any sockets left open
        /// </summary>
        private void LogOut()
        {
            //check to see if the sock is non existant
            if (_ftpSocket != null)
            {
                //since its not we need to
                //close it and dispose of it
                _ftpSocket.Close();
                _ftpSocket = null;
            }
            //log the user out
            IsLoggedIn = false;
            Log.Info("User Logged Out");
        }
    }
}
