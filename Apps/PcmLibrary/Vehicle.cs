﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// From the application's perspective, this class is the API to the vehicle.
    /// </summary>
    /// <remarks>
    /// Methods in this class are high-level operations like "get the VIN," or "read the contents of the EEPROM."
    /// </remarks>
    class Vehicle : IDisposable
    {
        /// <summary>
        /// How many times we should attempt to send a message before giving up.
        /// </summary>
        private const int MaxSendAttempts = 10;

        /// <summary>
        /// How many times we should attempt to receive a message before giving up.
        /// </summary>
        /// <remarks>
        /// 10 is too small for the case when we get a bunch of "chatter 
        /// suppressed" messages right before trying to upload the kernel.
        /// Might be worth making this a parameter to the retry loops since
        /// in most cases when only need about 5.
        /// </remarks>
        private const int MaxReceiveAttempts = 15;

        /// <summary>
        /// The device we'll use to talk to the PCM.
        /// </summary>
        private Device device;

        /// <summary>
        /// This class knows how to generate message to send to the PCM.
        /// </summary>
        private MessageFactory messageFactory;

        /// <summary>
        /// This class knows how to parse the messages that come in from the PCM.
        /// </summary>
        private MessageParser messageParser;

        /// <summary>
        /// This is how we send user-friendly status messages and developer-oriented debug messages to the UI.
        /// </summary>
        private ILogger logger;

        public string DeviceDescription
        {
            get
            {
                return this.device.ToString();
            }
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public Vehicle(
            Device device, 
            MessageFactory messageFactory,
            MessageParser messageParser,
            ILogger logger)
        {
            this.device = device;
            this.messageFactory = messageFactory;
            this.messageParser = messageParser;
            this.logger = logger;
        }

        /// <summary>
        /// Finalizer.
        /// </summary>
        ~Vehicle()
        {
            this.Dispose(false);
        }

        /// <summary>
        /// Implements IDisposable.Dispose.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Part of the Dispose pattern.
        /// </summary>
        protected void Dispose(bool isDisposing)
        {
            if (this.device != null)
            {
                this.device.Dispose();
                this.device = null;
            }
        }

        /// <summary>
        /// Re-initialize the device.
        /// </summary>
        public async Task<bool> ResetConnection()
        {
            return await this.device.Initialize();
        }

        /// <summary>
        /// Query the PCM's VIN.
        /// </summary>
        public async Task<Response<string>> QueryVin()
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            this.device.ClearMessageQueue();

            if (!await this.device.SendMessage(this.messageFactory.CreateVinRequest1()))
            {
                return Response.Create(ResponseStatus.Timeout, "Unknown. Request for block 1 failed.");
            }

            Message response1 = await this.device.ReceiveMessage();
            if (response1 == null)
            {
                return Response.Create(ResponseStatus.Timeout, "Unknown. No response to request for block 1.");
            }

            if (!await this.device.SendMessage(this.messageFactory.CreateVinRequest2()))
            {
                return Response.Create(ResponseStatus.Timeout, "Unknown. Request for block 2 failed.");
            }

            Message response2 = await this.device.ReceiveMessage();
            if (response2 == null)
            {
                return Response.Create(ResponseStatus.Timeout, "Unknown. No response to request for block 2.");
            }

            if (!await this.device.SendMessage(this.messageFactory.CreateVinRequest3()))
            {
                return Response.Create(ResponseStatus.Timeout, "Unknown. Request for block 3 failed.");
            }

            Message response3 = await this.device.ReceiveMessage();
            if (response3 == null)
            {
                return Response.Create(ResponseStatus.Timeout, "Unknown. No response to request for block 3.");
            }

            return this.messageParser.ParseVinResponses(response1.GetBytes(), response2.GetBytes(), response3.GetBytes());
        }

        /// <summary>
        /// Query the PCM's Serial Number.
        /// </summary>
        public async Task<Response<string>> QuerySerial()
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            this.device.ClearMessageQueue();

            if (!await this.device.SendMessage(this.messageFactory.CreateSerialRequest1()))
            {
                return Response.Create(ResponseStatus.Timeout, "Unknown. Request for block 1 failed.");
            }

            Message response1 = await this.device.ReceiveMessage();
            if (response1 == null)
            {
                return Response.Create(ResponseStatus.Timeout, "Unknown. No response to request for block 1.");
            }

            if (!await this.device.SendMessage(this.messageFactory.CreateSerialRequest2()))
            {
                return Response.Create(ResponseStatus.Timeout, "Unknown. Request for block 2 failed.");
            }

            Message response2 = await this.device.ReceiveMessage();
            if (response2 == null)
            {
                return Response.Create(ResponseStatus.Timeout, "Unknown. No response to request for block 2.");
            }

            if (!await this.device.SendMessage(this.messageFactory.CreateSerialRequest3()))
            {
                return Response.Create(ResponseStatus.Timeout, "Unknown. Request for block 3 failed.");
            }

            Message response3 = await this.device.ReceiveMessage();
            if (response3 == null)
            {
                return Response.Create(ResponseStatus.Timeout, "Unknown. No response to request for block 3.");
            }

            return this.messageParser.ParseSerialResponses(response1, response2, response3);
        }

        /// <summary>
        /// Query the PCM's Broad Cast Code.
        /// </summary>
        public async Task<Response<string>> QueryBCC()
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            var query = this.CreateQuery(
                this.messageFactory.CreateBCCRequest,
                this.messageParser.ParseBCCresponse);

            return await query.Execute();
        }

        /// <summary>
        /// Query the PCM's Manufacturer Enable Counter (MEC)
        /// </summary>
        public async Task<Response<string>> QueryMEC()
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            var query = this.CreateQuery(
                this.messageFactory.CreateMECRequest,
                this.messageParser.ParseMECresponse);

            return await query.Execute();
        }

        /// <summary>
        /// Update the PCM's VIN
        /// </summary>
        /// <remarks>
        /// Requires that the PCM is already unlocked
        /// </remarks>
        public async Task<Response<bool>> UpdateVin(string vin)
        {
            this.device.ClearMessageQueue();

            if (vin.Length != 17) // should never happen, but....
            {
                this.logger.AddUserMessage("VIN " + vin + " is not 17 characters long!");
                return Response.Create(ResponseStatus.Error, false);
            }

            this.logger.AddUserMessage("Changing VIN to " + vin);

            byte[] bvin = Encoding.ASCII.GetBytes(vin);
            byte[] vin1 = new byte[6] { 0x00, bvin[0], bvin[1], bvin[2], bvin[3], bvin[4] };
            byte[] vin2 = new byte[6] { bvin[5], bvin[6], bvin[7], bvin[8], bvin[9], bvin[10] };
            byte[] vin3 = new byte[6] { bvin[11], bvin[12], bvin[13], bvin[14], bvin[15], bvin[16] };

            this.logger.AddUserMessage("Block 1");
            Response<bool> block1 = await WriteBlock(BlockId.Vin1, vin1);
            if (block1.Status != ResponseStatus.Success) return Response.Create(ResponseStatus.Error, false);
            this.logger.AddUserMessage("Block 2");
            Response<bool> block2 = await WriteBlock(BlockId.Vin2, vin2);
            if (block2.Status != ResponseStatus.Success) return Response.Create(ResponseStatus.Error, false);
            this.logger.AddUserMessage("Block 3");
            Response<bool> block3 = await WriteBlock(BlockId.Vin3, vin3);
            if (block3.Status != ResponseStatus.Success) return Response.Create(ResponseStatus.Error, false);

            return Response.Create(ResponseStatus.Success, true);
        }

        /// <summary>
        /// Query the PCM's operating system ID.
        /// </summary>
        /// <returns></returns>
        public async Task<Response<UInt32>> QueryOperatingSystemId()
        {
            return await this.QueryUnsignedValue(this.messageFactory.CreateOperatingSystemIdReadRequest);
        }

        /// <summary>
        /// Query the PCM's Hardware ID.
        /// </summary>
        /// <remarks>
        /// Note that this is a software variable and my not match the hardware at all of the software runs.
        /// </remarks>
        /// <returns></returns>
        public async Task<Response<UInt32>> QueryHardwareId()
        {
            return await this.QueryUnsignedValue(this.messageFactory.CreateHardwareIdReadRequest);
        }

        /// <summary>
        /// Query the PCM's Hardware ID.
        /// </summary>
        /// <remarks>
        /// Note that this is a software variable and my not match the hardware at all of the software runs.
        /// </remarks>
        /// <returns></returns>
        public async Task<Response<UInt32>> QueryCalibrationId()
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            var query = this.CreateQuery(
                this.messageFactory.CreateCalibrationIdReadRequest,
                this.messageParser.ParseBlockUInt32);
            return await query.Execute();
        }

        /// <summary>
        /// Helper function to create Query objects.
        /// </summary>
        private Query<T> CreateQuery<T>(Func<Message> generator, Func<Message, Response<T>> filter)
        {
            return new Query<T>(this.device, generator, filter, this.logger);
        }

        /// <summary>
        /// Helper function for queries that return unsigned 32-bit integers.
        /// </summary>
        private async Task<Response<UInt32>> QueryUnsignedValue(Func<Message> generator)
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            var query = this.CreateQuery(generator, this.messageParser.ParseBlockUInt32);
            return await query.Execute();
        }

        /// <summary>
        /// Suppres chatter on the VPW bus.
        /// </summary>
        public async Task SuppressChatter()
        {
            this.logger.AddDebugMessage("Suppressing VPW chatter.");
            Message suppressChatter = this.messageFactory.CreateDisableNormalMessageTransmission();
            await this.device.SendMessage(suppressChatter);
        }

        /// <summary>
        /// Try to send a message, retrying if necessary.
        /// </summary
        private async Task<bool> TrySendMessage(Message message, string description)
        {
            for (int attempt = 1; attempt <= MaxSendAttempts; attempt++)
            {
                if (await this.device.SendMessage(message))
                {
                    return true;
                }

                this.logger.AddDebugMessage("Unable to send " + description + " message. Attempt #" + attempt.ToString());
            }

            return false;
        }

        /// <summary>
        /// Unlock the PCM by requesting a 'seed' and then sending the corresponding 'key' value.
        /// </summary>
        public async Task<bool> UnlockEcu(int keyAlgorithm)
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            this.device.ClearMessageQueue();

            this.logger.AddDebugMessage("Sending seed request.");
            Message seedRequest = this.messageFactory.CreateSeedRequest();

            if (!await this.TrySendMessage(seedRequest, "seed request"))
            {
                this.logger.AddUserMessage("Unable to send seed request.");
                return false;
            }

            bool seedReceived = false;
            UInt16 seedValue = 0;

            for (int attempt = 1; attempt < MaxReceiveAttempts; attempt++)
            {
                Message seedResponse = await this.device.ReceiveMessage();
                if (seedResponse == null)
                {
                    this.logger.AddDebugMessage("No response to seed request.");
                    return false;
                }

                if (this.messageParser.IsUnlocked(seedResponse.GetBytes()))
                {
                    this.logger.AddUserMessage("PCM is already unlocked");
                    return true;
                }

                this.logger.AddDebugMessage("Parsing seed value.");
                Response<UInt16> seedValueResponse = this.messageParser.ParseSeed(seedResponse.GetBytes());
                if (seedValueResponse.Status == ResponseStatus.Success)
                {
                    seedValue = seedValueResponse.Value;
                    seedReceived = true;
                    break;
                }

                this.logger.AddDebugMessage("Unable to parse seed response. Attempt #" + attempt.ToString());
            }

            if (!seedReceived)
            {
                this.logger.AddUserMessage("No seed reponse received, unable to unlock PCM.");
                return false;
            }

            if (seedValue == 0x0000)
            {
                this.logger.AddUserMessage("PCM Unlock not required");
                return true;
            }

            UInt16 key = KeyAlgorithm.GetKey(keyAlgorithm, seedValue);

            this.logger.AddDebugMessage("Sending unlock request (" + seedValue.ToString("X4") + ", " + key.ToString("X4") + ")");
            Message unlockRequest = this.messageFactory.CreateUnlockRequest(key);
            if (!await this.TrySendMessage(unlockRequest, "unlock request"))
            {
                this.logger.AddDebugMessage("Unable to send unlock request.");
                return false;
            }

            for (int attempt = 1; attempt < MaxReceiveAttempts; attempt++)
            {
                Message unlockResponse = await this.device.ReceiveMessage();
                if (unlockResponse == null)
                {
                    this.logger.AddDebugMessage("No response to unlock request. Attempt #" + attempt.ToString());
                    continue;
                }

                string errorMessage;
                Response<bool> result = this.messageParser.ParseUnlockResponse(unlockResponse.GetBytes(), out errorMessage);
                if (errorMessage == null)
                {
                    return result.Value;
                }

                this.logger.AddUserMessage(errorMessage);
            }

            this.logger.AddUserMessage("Unable to process unlock response.");
            return false;
        }

        /// <summary>
        /// Writes a block of data to the PCM
        /// Requires an unlocked PCM
        /// </summary>
        private async Task<Response<bool>> WriteBlock(byte block, byte[] data)
        {
            Message m;
            Message ok = new Message(new byte[] { 0x6C, DeviceId.Tool, DeviceId.Pcm, 0x7B, block });

            switch (data.Length)
            {
                case 6:
                    m = new Message(new byte[] { 0x6C, DeviceId.Pcm, DeviceId.Tool, 0x3B, block, data[0], data[1], data[2], data[3], data[4], data[5] });
                    break;
                default:
                    logger.AddDebugMessage("Cant write block size " + data.Length);
                    return Response.Create(ResponseStatus.Error, false);
            }

            if (!await this.device.SendMessage(m))
            {
                logger.AddUserMessage("Failed to write block " + block + ", communications failure");
                return Response.Create(ResponseStatus.Error, false);
            }

            logger.AddDebugMessage("Successful write to block " + block);
            return Response.Create(ResponseStatus.Success, true);
        }

        public async Task<Response<byte[]>> LoadKernelFromFile(string path)
        {
            byte[] file = { 0x00 }; // dummy value

            if (path == "") return Response.Create(ResponseStatus.Error, file);

            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDirectory = Path.GetDirectoryName(exePath);
            path = Path.Combine(exeDirectory, path);

            try
            {
                using (Stream fileStream = File.OpenRead(path))
                {
                    file = new byte[fileStream.Length];

                    // In theory we might need a loop here. In practice, I don't think that will be necessary.
                    int bytesRead = await fileStream.ReadAsync(file, 0, (int)fileStream.Length);

                    if(bytesRead != fileStream.Length)
                    {
                        return Response.Create(ResponseStatus.Truncated, file);
                    }
                }
                
                logger.AddDebugMessage("Loaded " + path);
            }
            catch (ArgumentException)
            {
                logger.AddDebugMessage("Invalid file path " + path);
                return Response.Create(ResponseStatus.Error, file);
            }
            catch (PathTooLongException)
            {
                logger.AddDebugMessage("File path is too long " + path);
                return Response.Create(ResponseStatus.Error, file);
            }
            catch (DirectoryNotFoundException)
            {
                logger.AddDebugMessage("Invalid directory " + path);
                return Response.Create(ResponseStatus.Error, file);
            }
            catch (IOException)
            {
                logger.AddDebugMessage("Error accessing file " + path);
                return Response.Create(ResponseStatus.Error, file);
            }
            catch (UnauthorizedAccessException)
            {
                logger.AddDebugMessage("No permission to read file " + path);
                return Response.Create(ResponseStatus.Error, file);
            }

            return Response.Create(ResponseStatus.Success, file);
        }

        /// <summary>
        /// Read the full contents of the PCM.
        /// Assumes the PCM is unlocked an were ready to go
        /// </summary>
        public async Task<Response<Stream>> ReadContents(PcmInfo info, CancellationToken cancellationToken)
        {
            try
            {
                this.device.ClearMessageQueue();

                // This must precede the switch to 4X.
                ToolPresentNotifier toolPresentNotifier = new ToolPresentNotifier(this.logger, this.messageFactory, this.device);
                await toolPresentNotifier.Notify();

                // switch to 4x, if possible. But continue either way.
                // if the vehicle bus switches but the device does not, the bus will need to time out to revert back to 1x, and the next steps will fail.
                if (!await this.VehicleSetVPW4x(VpwSpeed.FourX))
                {
                    this.logger.AddUserMessage("Stopping here because we were unable to switch to 4X.");
                    return Response.Create(ResponseStatus.Error, (Stream)null);
                }

                await toolPresentNotifier.Notify();

                // execute read kernel
                Response<byte[]> response = await LoadKernelFromFile("kernel.bin");
                if (response.Status != ResponseStatus.Success)
                {
                    logger.AddUserMessage("Failed to load kernel from file.");
                    return new Response<Stream>(response.Status, null);
                }
                
                if (cancellationToken.IsCancellationRequested)
                {
                    return Response.Create(ResponseStatus.Cancelled, (Stream)null);
                }

                await toolPresentNotifier.Notify();

                // TODO: instead of this hard-coded 0xFF913E, get the base address from the PcmInfo object.
                if (!await PCMExecute(response.Value, 0xFF913E, cancellationToken))
                {
                    logger.AddUserMessage("Failed to upload kernel to PCM");

                    return new Response<Stream>(
                        cancellationToken.IsCancellationRequested ? ResponseStatus.Cancelled : ResponseStatus.Error, 
                        null);
                }

                logger.AddUserMessage("kernel uploaded to PCM succesfully. Requesting data...");

                await this.device.SetTimeout(TimeoutScenario.ReadMemoryBlock);

                int startAddress = info.ImageBaseAddress;
                int endAddress = info.ImageBaseAddress + info.ImageSize;
                int bytesRemaining = info.ImageSize;
                int blockSize = this.device.MaxReceiveSize - 10 - 2; // allow space for the header and block checksum

                byte[] image = new byte[info.ImageSize];

                while (startAddress < endAddress)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Response.Create(ResponseStatus.Cancelled, (Stream)null);
                    }

                    await toolPresentNotifier.Notify();

                    if (startAddress + blockSize > endAddress)
                    {
                        blockSize = endAddress - startAddress;
                    }

                    if (blockSize < 1)
                    {
                        this.logger.AddUserMessage("Image download complete");
                        break;
                    }
                    
                    if (!await TryReadBlock(image, blockSize, startAddress))
                    {
                        this.logger.AddUserMessage(
                            string.Format(
                                "Unable to read block from {0} to {1}",
                                startAddress,
                                (startAddress + blockSize) - 1));
                        return new Response<Stream>(ResponseStatus.Error, null);
                    }

                    startAddress += blockSize;
                }

                await this.Cleanup(); // Not sure why this does not get called in the finally block on successfull read?

                MemoryStream stream = new MemoryStream(image);
                return new Response<Stream>(ResponseStatus.Success, stream);
            }
            catch(Exception exception)
            {
                this.logger.AddUserMessage("Something went wrong. " + exception.Message);
                this.logger.AddDebugMessage(exception.ToString());
                return new Response<Stream>(ResponseStatus.Error, null);
            }
            finally
            {
                // Sending the exit command at both speeds and revert to 1x.
                await this.Cleanup();
            }
        }

        /// <summary>
        /// Cleanup calls the various cleanup routines to get everything back to normal
        /// </summary>
        /// <remarks>
        /// Exit kernel at 4x, 1x, and clear DTCs
        /// </remarks>
        public async Task Cleanup()
        {
            this.logger.AddDebugMessage("Cleaning up Flash Kernel");
            await this.ExitKernel();
            this.logger.AddDebugMessage("Clear DTCs");
            await this.ClearDTCs();
        }

        /// <summary>
        /// Exits the kernel at 4x, then at 1x. Once this function has been called the bus will be back at 1x.
        /// </summary>
        /// <remarks>
        /// Can be used to force exit the kernel, if requied. Does not attempt the 4x exit if not supported by the current device.
        /// </remarks>
        public async Task ExitKernel()
        {
            Message exitKernel = this.messageFactory.CreateExitKernel();

            this.device.ClearMessageQueue();
            if (device.Supports4X)
            {
                await device.SetVpwSpeed(VpwSpeed.FourX);
                await this.device.SendMessage(exitKernel);
                await device.SetVpwSpeed(VpwSpeed.Standard);
            }

            await this.device.SendMessage(exitKernel);
        }

        /// <summary>
        /// Clears DTCs
        /// </summary>
        /// <remarks>
        /// Return code is not checked as its an uncommon mode and IDs, different devices will handle this differently.
        /// </remarks>
        public async Task ClearDTCs()
        {
            Message ClearDTCs = this.messageFactory.CreateClearDTCs();
            Message ClearDTCsOK = this.messageFactory.CreateClearDTCsOK();

            await this.device.SendMessage(ClearDTCs);
            this.device.ClearMessageQueue();
        }

        private async Task<bool> TryReadBlock(byte[] image, int length, int startAddress)
        {
            this.logger.AddDebugMessage(string.Format("Reading from {0}, length {1}", startAddress, length));
            
            for(int sendAttempt = 1; sendAttempt <= MaxSendAttempts; sendAttempt++)
            {
                Message message = this.messageFactory.CreateReadRequest(startAddress, length);

                //this.logger.AddDebugMessage("Sending " + message.GetBytes().ToHex());
                if (!await this.device.SendMessage(message))
                {
                    this.logger.AddDebugMessage("Unable to send read request.");
                    continue;
                }

                bool sendAgain = false;
                for (int receiveAttempt = 1; receiveAttempt <= MaxReceiveAttempts; receiveAttempt++)
                {
                    Message response = await this.ReceiveMessage();
                    if (response == null)
                    {
                        this.logger.AddDebugMessage("Did not receive a response to the read request.");
                        sendAgain = true;
                        break;
                    }

                    this.logger.AddDebugMessage("Processing message");

                    Response<bool> readResponse = this.messageParser.ParseReadResponse(response);
                    if (readResponse.Status != ResponseStatus.Success)
                    {
                        this.logger.AddDebugMessage("Not a read response.");
                        continue;
                    }

                    if (!readResponse.Value)
                    {
                        this.logger.AddDebugMessage("Read request failed.");
                        sendAgain = true;
                        break;
                    }

                    // We got a successful read response, so now wait for the payload.
                    sendAgain = false;
                    break;
                }

                if (sendAgain)
                {
                    continue;
                }

                this.logger.AddDebugMessage("Read request allowed, expecting for payload...");
                for (int receiveAttempt = 1; receiveAttempt <= MaxReceiveAttempts; receiveAttempt++)
                {   
                    Message payloadMessage = await this.device.ReceiveMessage();
                    if (payloadMessage == null)
                    {
                        this.logger.AddDebugMessage("No payload following read request.");
                        continue;
                    }

                    this.logger.AddDebugMessage("Processing message");

                    Response<byte[]> payloadResponse = this.messageParser.ParsePayload(payloadMessage, length, startAddress);
                    if (payloadResponse.Status != ResponseStatus.Success)
                    {
                        this.logger.AddDebugMessage("Not a valid payload message or bad checksum");
                        continue;
                    }

                    byte[] payload = payloadResponse.Value;
                    Buffer.BlockCopy(payload, 0, image, startAddress, length);

                    int percentDone = (startAddress * 100) / image.Length;
                    this.logger.AddUserMessage(string.Format("Recieved block starting at {0} / 0x{0:X}. {1}%", startAddress, percentDone));

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Wait for an incoming message.
        /// </summary>
        private async Task<Message> ReceiveMessage()
        {
            Message response = null;

            for (int pause = 0; pause < 10; pause++)
            {
                response = await this.device.ReceiveMessage();
                if (response == null)
                {
                    this.logger.AddDebugMessage("No response to read request yet.");
                    await Task.Delay(10);
                    continue;
                }

                break;
            }

            return response;
        }

        /// <summary>
        /// Replace the full contents of the PCM.
        /// </summary>
        public Task<bool> WriteContents(Stream stream)
        {
            return Task.FromResult(true);
        }

        /// <summary>
        /// Read messages from the device, ignoring irrelevant messages.
        /// </summary>
        private async Task<bool> WaitForSuccess(Func<Message, Response<bool>> filter)
        {
            for(int attempt = 1; attempt<=MaxReceiveAttempts; attempt++)
            {
                Message message = await this.device.ReceiveMessage();
                if(message == null)
                {
                    continue;
                }

                Response<bool> response = filter(message);
                if (response.Status != ResponseStatus.Success)
                {
                    this.logger.AddDebugMessage("Ignoring unrelated message.");
                    continue;
                }

                this.logger.AddDebugMessage("Found response, " + (response.Value ? "succeeded." : "failed."));
                return response.Value;
            }

            return false;
        }

        /// <summary>
        /// Load the executable payload on the PCM at the supplied address, and execute it.
        /// </summary>
        public async Task<bool> PCMExecute(byte[] payload, int address, CancellationToken cancellationToken)
        {
            logger.AddUserMessage("Uploading kernel to PCM.");

            logger.AddDebugMessage("Sending upload request with payload size " + payload.Length + ", loadaddress " + address.ToString("X6"));
            Message request = messageFactory.CreateUploadRequest(payload.Length, address);

            if(!await TrySendMessage(request, "upload request"))
            {
                return false;
            }

            if (!await this.WaitForSuccess(this.messageParser.ParseUploadPermissionResponse))
            {
                logger.AddUserMessage("Permission to upload kernel was denied.");
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            logger.AddDebugMessage("Going to load a " + payload.Length + " byte payload to 0x" + address.ToString("X6"));

            await this.device.SetTimeout(TimeoutScenario.SendKernel);

            // Loop through the payload building and sending packets, highest first, execute on last
            int payloadSize = device.MaxSendSize - 12; // Headers use 10 bytes, sum uses 2 bytes.
            int chunkCount = payload.Length / payloadSize;
            int remainder = payload.Length % payloadSize;

            int offset = (chunkCount * payloadSize);
            int startAddress = address + offset;

            // First we send the 'remainder' payload, containing any bytes that won't fill up an entire upload packet.
            logger.AddDebugMessage(
                string.Format(
                    "Sending remainder payload with offset 0x{0:X}, start address 0x{1:X}, length 0x{2:X}.",
                    offset,
                    startAddress,
                    remainder));

            Message remainderMessage = messageFactory.CreateBlockMessage(
                payload, 
                offset, 
                remainder, 
                address + offset, 
                remainder == payload.Length);

            Response<bool> uploadResponse = await WriteToRam(remainderMessage);
            if (uploadResponse.Status != ResponseStatus.Success)
            {
                logger.AddDebugMessage("Could not upload kernel to PCM, remainder payload not accepted.");
                return false;
            }

            // Now we send a series of full upload packets
            for (int chunkIndex = chunkCount; chunkIndex > 0; chunkIndex--)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                offset = (chunkIndex - 1) * payloadSize;
                startAddress = address + offset;
                Message payloadMessage = messageFactory.CreateBlockMessage(
                    payload,
                    offset,
                    payloadSize,
                    startAddress,
                    offset == 0);

                logger.AddDebugMessage(
                    string.Format(
                        "Sending payload with offset 0x{0:X}, start address 0x{1:X}, length 0x{2:X}.",
                        offset,
                        startAddress,
                        payloadSize));

                uploadResponse = await WriteToRam(payloadMessage);
                if (uploadResponse.Status != ResponseStatus.Success)
                {
                    logger.AddDebugMessage("Could not upload kernel to PCM, payload not accepted.");
                    return false;
                }

                int bytesSent = payload.Length - offset;
                int percentDone = bytesSent * 100 / payload.Length;

                this.logger.AddUserMessage(
                    string.Format(
                        "Kernel upload {0}% complete.",
                        percentDone));
            }

            return true;
        }

        /// <summary>
        /// Does everything required to switch to VPW 4x
        /// </summary>
        public async Task<bool> VehicleSetVPW4x(VpwSpeed newSpeed)
        {
            if (!device.Supports4X) 
            {
                if (newSpeed == VpwSpeed.FourX)
                {
                    // where there is no support only report no switch to 4x
                    logger.AddUserMessage("This interface does not support VPW 4x");
                }
                return true;
            }
            
            // Configure the vehicle bus when switching to 4x
            if (newSpeed == VpwSpeed.FourX)
            {
                logger.AddUserMessage("Attempting switch to VPW 4x");
                await device.SetTimeout(TimeoutScenario.ReadProperty);

                // The list of modules may not be useful after all, but 
                // checking for an empty list indicates an uncooperative
                // module on the VPW bus.
                List<byte> modules = await this.RequestHighSpeedPermission();
                if (modules == null)
                {
                    // A device has refused the switch to high speed mode.
                    return false;
                }

                Message broadcast = this.messageFactory.CreateBeginHighSpeed(DeviceId.Broadcast);
                await this.device.SendMessage(broadcast);

                // Check for any devices that refused to switch to 4X speed.
                // These responses usually get lost, so this code might be pointless.
                Message response = null;
                while ((response = await this.device.ReceiveMessage()) != null)
                {
                    Response<bool> refused = this.messageParser.ParseHighSpeedRefusal(response);
                    if (refused.Status != ResponseStatus.Success)
                    {
                        continue;
                    }

                    if (refused.Value == false)
                    {
                        // TODO: Add module number.
                        this.logger.AddUserMessage("Module refused high-speed switch.");
                        return false;
                    }
                }
            }
            else
            {
                logger.AddUserMessage("Reverting to VPW 1x");
            }

            // Request the device to change
            await device.SetVpwSpeed(newSpeed);

            TimeoutScenario scenario = newSpeed == VpwSpeed.Standard ? TimeoutScenario.ReadProperty : TimeoutScenario.ReadMemoryBlock;
            await device.SetTimeout(scenario);

            return true;
        }
        
        /// <summary>
        /// Ask all of the devices on the VPW bus for permission to switch to 4X speed.
        /// </summary>
        private async Task<List<byte>> RequestHighSpeedPermission()
        {
            Message permissionCheck = this.messageFactory.CreateHighSpeedPermissionRequest(DeviceId.Broadcast);
            await this.device.SendMessage(permissionCheck);

            // Note that as of right now, the AllPro only receives 6 of the 11 responses.
            // So until that gets fixed, we could miss a 'refuse' response and try to switch
            // to 4X anyhow. That just results in an aborted read attempt, with no harm done.
            List<byte> result = new List<byte>();
            Message response = null;
            bool anyRefused = false;
            while ((response = await this.device.ReceiveMessage()) != null)
            {
                this.logger.AddDebugMessage("Parsing " + response.GetBytes().ToHex());
                MessageParser.HighSpeedPermissionResult parsed = this.messageParser.ParseHighSpeedPermissionResponse(response);
                if (!parsed.IsValid)
                {
                    continue;
                }

                result.Add(parsed.DeviceId);

                if (parsed.PermissionGranted)
                {
                    this.logger.AddUserMessage(string.Format("Module 0x{0:X2} ({1}) has agreed to enter high-speed mode.", parsed.DeviceId, DeviceId.DeviceCategory(parsed.DeviceId)));
                    continue;
                }

                this.logger.AddUserMessage(string.Format("Module 0x{0:X2} ({1}) has refused to enter high-speed mode.", parsed.DeviceId, DeviceId.DeviceCategory(parsed.DeviceId)));
                anyRefused = true;
            }
            
            if (anyRefused)
            {
                return null;
            }

            return result;
        }

        /// <summary>
        /// Sends the provided message retries times, with a small delay on fail. 
        /// </summary>
        /// <remarks>
        /// Returns a succsefull Response on the first successful attempt, or the failed Response if we run out of tries.
        /// </remarks>
        async Task<Response<bool>> WriteToRam(Message message)
        {
            for (int i = MaxSendAttempts; i>0; i--)
            {
                if (!await device.SendMessage(message))
                {
                    this.logger.AddDebugMessage("WriteToRam: Unable to send message.");
                    continue;
                }

                if (await this.WaitForSuccess(this.messageParser.ParseUploadResponse))
                {
                    return Response.Create(ResponseStatus.Success, true);
                }

                this.logger.AddDebugMessage("WriteToRam: Upload request failed.");
            }

            this.logger.AddDebugMessage("WriteToRam: Giving up.");
            return Response.Create(ResponseStatus.Error, false); // this should be response from the loop but the compiler thinks the response variable isnt in scope here????
        }
    }
}
