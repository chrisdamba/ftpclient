using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FTPClient
{
    class Program
    {
        static void Main(string[] args)
        {
            var server = args[0];
            var path = args[1];
            var username = args[2];
            var password = args[3];

            var ftpService = new FtpService(server, path, username, password);

            ftpService.FtpLogin();

            ftpService.Rename("MainForm.cs", "test.poi", true);

            ftpService.CloseConnection();
        }
    }
}
