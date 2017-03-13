using System;
using System.Collections.ObjectModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Data.Sqlite.Internal;


namespace SerialSample
{
    public sealed partial class MainPage : Page
    {
        /// <summary>
        /// Private variables
        /// </summary>
       
        private SerialDevice serialPort = null;
        DataReader dataReaderObject = null;

        private ObservableCollection<DeviceInformation> listOfDevices;
        private CancellationTokenSource ReadCancellationTokenSource;
       
        public MainPage()
        {
            this.InitializeComponent();            
            comPortInput.IsEnabled = false;
            listOfDevices = new ObservableCollection<DeviceInformation>();
            ListAvailablePorts();
        }

        /// <summary>
        /// ListAvailablePorts
        /// - Use SerialDevice.GetDeviceSelector to enumerate all serial devices
        /// - Attaches the DeviceInformation to the ListBox source so that DeviceIds are displayed
        /// </summary>
        private async void ListAvailablePorts()
        {
            try
            {
                string aqs = SerialDevice.GetDeviceSelector();
                var dis = await DeviceInformation.FindAllAsync(aqs);

                status.Text = "Select a device and connect";

                for (int i = 0; i < dis.Count; i++)
                {
                    listOfDevices.Add(dis[i]);
                }

                DeviceListSource.Source = listOfDevices;
                comPortInput.IsEnabled = true;
                ConnectDevices.SelectedIndex = -1;
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
        }

        /// <summary>
        /// comPortInput_Click: Action to take when 'Connect' button is clicked
        /// - Get the selected device index and use Id to create the SerialDevice object
        /// - Configure default settings for the serial port
        /// - Create the ReadCancellationTokenSource token
        /// - Start listening on the serial port input
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void comPortInput_Click(object sender, RoutedEventArgs e)
        {
            var selection = ConnectDevices.SelectedItems;

            if (selection.Count <= 0)
            {
                status.Text = "Select a device and connect";
                return;
            }

            DeviceInformation entry = (DeviceInformation)selection[0];         

            try
            {                
                serialPort = await SerialDevice.FromIdAsync(entry.Id);
              
                // Disable the 'Connect' button 
                comPortInput.IsEnabled = false;

                // Configure serial settings
                serialPort.ReadTimeout = TimeSpan.FromMilliseconds(1000);                
                serialPort.BaudRate = 1200;
                serialPort.Parity = SerialParity.Even;
                serialPort.StopBits = SerialStopBitCount.One;
                serialPort.DataBits = 7;
                serialPort.Handshake = SerialHandshake.None;

                // Display configured settings
                status.Text = "Serial port configured successfully: ";
                status.Text += serialPort.BaudRate + "-";
                status.Text += serialPort.DataBits + "-";
                status.Text += serialPort.Parity.ToString() + "-";
                status.Text += serialPort.StopBits;
          
                // Set the RcvdText field to invoke the TextChanged callback
                // The callback launches an async Read task to wait for data
                status.Text = "Waiting for data...";
              
                // Create cancellation token object to close I/O operations when closing the device
                ReadCancellationTokenSource = new CancellationTokenSource();
                

                Listen();
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                comPortInput.IsEnabled = true;
            }
        }
        

        /// <summary>
        /// - Create a DataReader object
        /// - Create an async task to read from the SerialDevice InputStream
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Listen()
        {
            try
            {
                if (serialPort != null)
                {
                    dataReaderObject = new DataReader(serialPort.InputStream);
                  
                    // keep reading the serial input
                    while (true)
                    {
                        await ReadAsync(ReadCancellationTokenSource.Token);
                    }
                }
            }
            catch (TaskCanceledException )            {
                status.Text = "Reading task was cancelled, closing device and cleaning up";
                CloseDevice();            
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
            finally
            {
                // Cleanup once complete
                if (dataReaderObject != null)
                {
                    dataReaderObject.DetachStream();
                    dataReaderObject = null;
                }
            }
        }

        /// <summary>
        /// ReadAsync: Task that waits on data and reads asynchronously from the serial device InputStream
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        String trame = "";
        private async Task ReadAsync(CancellationToken cancellationToken)
        {
            Task<UInt32> loadAsyncTask;

            uint ReadBufferLength = 1;

            // If task cancellation was requested, comply
            cancellationToken.ThrowIfCancellationRequested();

            // Set InputStreamOptions to complete the asynchronous read operation when one or more bytes is available
            dataReaderObject.InputStreamOptions = InputStreamOptions.Partial;

            // Create a task object to wait for data on the serialPort.InputStream
            loadAsyncTask = dataReaderObject.LoadAsync(ReadBufferLength).AsTask(cancellationToken);

            // Launch the task and wait
            UInt32 bytesRead = await loadAsyncTask;
            if (bytesRead > 0)
            {
                //rcvdText.Text += dataReaderObject.ReadString(bytesRead);
                String charactereRecu = dataReaderObject.ReadString(bytesRead);
                if (charactereRecu == "\n")
                {
                    // On ignore le \n on ne fait strictement rien ici il ne faut pas supprimer ce bloc c'est la ligne \r qui gère le cas \r\n
                }
                else if (charactereRecu == "\r")// J'ai reçu un \r, je suis donc sur un fin de trame, j'ignore donc ce caractère et le suivant
                {
                    // On traite la trame reçue 
                    rcvdText.Text += trame + Environment.NewLine;
                    string[] Result;
                    Result = trame.Split(' ');
                    Resultat.Text = Result[0] + " " + Result[1] + " ";
                    SaveToBdd(Result[0], Result[1]);

         

                    // On vide la trame
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    trame = "";
                    Resultat.Text = "";
                }
                else // cas général
                {
                    trame += charactereRecu;
                }

                status.Text = "bytes read successfully!";
            }
        }  //la trame est lue et traitée
            
        

        /// <summary>
        /// CancelReadTask:
        /// - Uses the ReadCancellationTokenSource to cancel read operations
        /// </summary>
        private void CancelReadTask()
        {         
            if (ReadCancellationTokenSource != null)
            {
                if (!ReadCancellationTokenSource.IsCancellationRequested)
                {
                    ReadCancellationTokenSource.Cancel();
                }
            }         
        }

        /// <summary>
        /// CloseDevice:
        /// - Disposes SerialDevice object
        /// - Clears the enumerated device Id list
        /// </summary>
        private void CloseDevice()
        {            
            if (serialPort != null)
            {
                serialPort.Dispose();
            }
            serialPort = null;

            comPortInput.IsEnabled = true;         
            rcvdText.Text = "";
            listOfDevices.Clear();               
        }

        /// <summary>
        /// closeDevice_Click: Action to take when 'Disconnect and Refresh List' is clicked on
        /// - Cancel all read operations
        /// - Close and dispose the SerialDevice object
        /// - Enumerate connected devices
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void closeDevice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                status.Text = "";
                CancelReadTask();
                CloseDevice();
                ListAvailablePorts();
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }          
        }

        private void ConnectDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void pageTitle_SelectionChanged(object sender, RoutedEventArgs e)
        {

        }

     
            private void SaveToBdd(String type, String value)
        {
            
            SqliteEngine.UseWinSqlite3(); //Configuring library to use SDK version of SQLite
            using (SqliteConnection db = new SqliteConnection("Filename=edf.db"))
            {
                db.Open();
                String sql = "UPDATE EDF SET "+type+" = '"+value+"'";
                SqliteCommand command = new SqliteCommand(sql, db);
                try
                {
                    command.ExecuteReader();
                    status.Text = "Save BDD OK";
                }
                catch (SqliteException)
                {
                    //Do nothing
                    status.Text = "Erreur de sauvegarde dans la BDD";

                }
                
            }
        }  // la trame est sauvée en bdd avec sqlite
    }
    }

