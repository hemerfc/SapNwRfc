using System;
using System.IO;
using SapNwRfc.Internal;
using SapNwRfc.Internal.Interop;

namespace SapNwRfc
{
    /// <summary>
    /// Represents an SAP RFC server.
    /// </summary>
    public sealed class SapServer : ISapServer
    {
        private readonly RfcInterop _interop;
        private readonly IntPtr _rfcServerHandle;
        private readonly SapConnectionParameters _parameters;

        private SapServer(RfcInterop interop, IntPtr rfcServerHandle, SapConnectionParameters parameters)
        {
            _interop = interop;
            _rfcServerHandle = rfcServerHandle;
            _parameters = parameters;
        }

        /// <summary>
        /// Creates and connects a new RFC Server.
        /// </summary>
        /// <param name="connectionString">The connection string.</param>
        /// <returns>The SAP RFC Server.</returns>
        public static ISapServer Create(string connectionString)
        {
            return Create(SapConnectionParameters.Parse(connectionString));
        }

        /// <summary>
        /// Creates and connects a new RFC Server.
        /// </summary>
        /// <param name="parameters">The connection parameters.</param>
        /// <returns>The SAP RFC Server.</returns>
        public static ISapServer Create(SapConnectionParameters parameters)
        {
            return Create(new RfcInterop(), parameters);
        }

        private static ISapServer Create(RfcInterop rfcInterop, SapConnectionParameters parameters)
        {
            RfcConnectionParameter[] interopParameters = parameters.ToInterop();

            IntPtr rfcServerHandle = rfcInterop.CreateServer(
                connectionParams: interopParameters,
                paramCount: (uint)interopParameters.Length,
                errorInfo: out RfcErrorInfo errorInfo);

            errorInfo.ThrowOnError();

            return new SapServer(rfcInterop, rfcServerHandle, parameters);
        }

        private EventHandler<SapServerErrorEventArgs> _error;

        /// <inheritdoc cref="ISapServer"/>
        public event EventHandler<SapServerErrorEventArgs> Error
        {
            add
            {
                if (_error == null)
                {
                    RfcResultCode resultCode = _interop.AddServerErrorListener(
                        rfcHandle: _rfcServerHandle,
                        errorListener: ServerErrorListener,
                        errorInfo: out RfcErrorInfo errorInfo);

                    resultCode.ThrowOnError(errorInfo);
                }

                _error += value;
            }

            remove
            {
                _error -= value;
            }
        }

        private void ServerErrorListener(IntPtr serverHandle, in RfcAttributes clientInfo, in RfcErrorInfo errorInfo)
        {
            _error?.Invoke(this, new SapServerErrorEventArgs(new SapAttributes(clientInfo), new SapErrorInfo(errorInfo)));
        }

        private EventHandler<SapServerStateChangeEventArgs> _stateChange;

        /// <inheritdoc cref="ISapServer"/>
        public event EventHandler<SapServerStateChangeEventArgs> StateChange
        {
            add
            {
                if (_stateChange == null)
                {
                    RfcResultCode resultCode = _interop.AddServerStateChangedListener(
                        rfcHandle: _rfcServerHandle,
                        stateChangeListener: ServerStateChangeListener,
                        errorInfo: out RfcErrorInfo errorInfo);

                    resultCode.ThrowOnError(errorInfo);
                }

                _stateChange += value;
            }

            remove
            {
                _stateChange -= value;
            }
        }

        private void ServerStateChangeListener(IntPtr serverHandle, in RfcStateChange stateChange)
        {
            _stateChange?.Invoke(this, new SapServerStateChangeEventArgs(stateChange));
        }

        /// <inheritdoc cref="ISapServer"/>
        public void Launch()
        {
            RfcResultCode resultCode = _interop.LaunchServer(
                rfcHandle: _rfcServerHandle,
                errorInfo: out RfcErrorInfo errorInfo);

            resultCode.ThrowOnError(errorInfo);
        }

        /// <inheritdoc cref="ISapServer"/>
        public void Shutdown(uint timeout)
        {
            RfcResultCode resultCode = _interop.ShutdownServer(
                rfcHandle: _rfcServerHandle,
                timeout: timeout,
                errorInfo: out RfcErrorInfo errorInfo);

            resultCode.ThrowOnError(errorInfo);
        }

        /// <summary>
        /// Disposes the server. Disposing automatically disconnects from the SAP application server.
        /// </summary>
        public void Dispose()
        {
            _interop.DestroyServer(
                rfcHandle: _rfcServerHandle,
                errorInfo: out RfcErrorInfo _);
        }

        /// <summary>
        /// Installs a global server function handler.
        /// </summary>
        /// <param name="connectionString">The connection string.</param>
        /// <param name="action">The RFC server function handler.</param>
        public static void InstallGenericServerFunctionHandler(string connectionString, Action<ISapServerConnection, ISapServerFunction> action)
        {
            InstallGenericServerFunctionHandler(new RfcInterop(), SapConnectionParameters.Parse(connectionString), action);
        }

        /// <summary>
        /// Installs a global server function handler.
        /// </summary>
        /// <param name="connectionString">The connection string.</param>
        /// <param name="action">The RFC server function handler.</param>
        /// <param name="repositoryFilePath">The repository file name.</param>
        /// <param name="repoName">The repository name.</param>
        public static void InstallGenericServerFunctionHandler(
            string connectionString,
            Action<ISapServerConnection, ISapServerFunction> action, string repositoryFilePath, string repoName)
        {
            InstallGenericServerFunctionHandler(new RfcInterop(), SapConnectionParameters.Parse(connectionString), action, repositoryFilePath, repoName);
        }

        #pragma warning disable SA1306 // Field names should begin with lower-case letter
        private static RfcInterop.RfcServerFunction CurrentServerFunction;
        private static RfcInterop.RfcFunctionDescriptionCallback CurrentFunctionDescriptionCallback;
        private static RfcInterop.RfcFunctionDescriptionCallback CurrentFunctionDescriptionFromCacheCallback;
        #pragma warning restore SA1306 // Field names should begin with lower-case letter

        private static void InstallGenericServerFunctionHandler(RfcInterop interop, SapConnectionParameters parameters, Action<ISapServerConnection, ISapServerFunction> action)
        {
            CurrentServerFunction = (IntPtr connectionHandle, IntPtr functionHandle, out RfcErrorInfo errorInfo)
                => HandleGenericFunction(interop, action, connectionHandle, functionHandle, out errorInfo);

            CurrentFunctionDescriptionCallback = (string functionName, RfcAttributes attributes, ref IntPtr funcDescHandle)
                => HandleGenericMetadata(interop, parameters, functionName, out funcDescHandle);

            RfcResultCode resultCode = interop.InstallGenericServerFunction(
                serverFunction: CurrentServerFunction,
                funcDescPointer: CurrentFunctionDescriptionCallback,
                out RfcErrorInfo installFunctionErrorInfo);

            resultCode.ThrowOnError(installFunctionErrorInfo);
        }

        private static void InstallGenericServerFunctionHandler(RfcInterop interop, SapConnectionParameters parameters,
            Action<ISapServerConnection, ISapServerFunction> action, string repositoryFileName, string repoName)
        {
            CurrentServerFunction = (IntPtr connectionHandle, IntPtr functionHandle, out RfcErrorInfo errorInfo)
                => HandleGenericFunction(interop, action, connectionHandle, functionHandle, out errorInfo);

            CurrentFunctionDescriptionFromCacheCallback =
                (string functionName2, RfcAttributes attributes, ref IntPtr funcDescHandle) =>
                {
                    LoadRepositoryFromFile(interop, repositoryFileName, repoName);

                    funcDescHandle = interop.GetCachedFunctionDesc(
                        repositoryId: repoName,
                        funcName: functionName2,
                        errorInfo: out RfcErrorInfo errorInfo2);

                    errorInfo2.ThrowOnError();

                    return RfcResultCode.RFC_OK;
                };

            /* CurrentFunctionDescriptionCallback = (string functionName_, RfcAttributes attributes, ref IntPtr funcDescHandle)
              => HandleGenericMetadata(interop, parameters, functionName_, out funcDescHandle); */

            RfcResultCode resultCode = interop.InstallGenericServerFunction(
                serverFunction: CurrentServerFunction,
                funcDescPointer: CurrentFunctionDescriptionFromCacheCallback,
                out RfcErrorInfo installFunctionErrorInfo);

            resultCode.ThrowOnError(installFunctionErrorInfo);
        }

        /// <inheritdoc cref="ISapConnection"/>
        private static bool LoadRepositoryFromFile(RfcInterop interop, string filePath, string repositoryId)
        {
            // open repository file for read
            IntPtr filePtr = LegacyFileManager.fopen(filePath, "r");
            if (filePtr == IntPtr.Zero)
            {
                throw new IOException($"Failed to open repository file '{filePath}'.");
            }

            RfcResultCode resultCode = interop.LoadRepository(
                repositoryId: repositoryId,
                targetStream: filePtr,
                errorInfo: out RfcErrorInfo errorInfo);

            LegacyFileManager.fclose(filePtr);

            errorInfo.ThrowOnError();

            return resultCode == RfcResultCode.RFC_OK;
        }

        /// <inheritdoc cref="ISapConnection"/>
        private static bool SaveRepositoryFromFile(RfcInterop interop, string filePath, string repositoryId)
        {
            // open repository file for read
            IntPtr filePtr = LegacyFileManager.fopen(filePath, "w");
            if (filePtr == IntPtr.Zero)
            {
                throw new IOException($"Failed to open repository file '{filePath}'.");
            }

            RfcResultCode resultCode = interop.SaveRepository(
                repositoryId: repositoryId,
                targetStream: filePtr,
                errorInfo: out RfcErrorInfo errorInfo);

            LegacyFileManager.fclose(filePtr);

            errorInfo.ThrowOnError();

            return resultCode == RfcResultCode.RFC_OK;
        }

        /*
        private static ISapFunction CreateCachedFunction(RfcInterop interop, string name, string repositoryId)
        {
            IntPtr functionDescriptionHandle = interop.GetCachedFunctionDesc(
                repositoryId: repositoryId,
                funcName: name,
                errorInfo: out RfcErrorInfo errorInfo);

            errorInfo.ThrowOnError();

            return SapFunction.CreateFromDescriptionHandle(
                interop: _interop,
                rfcConnectionHandle: _rfcConnectionHandle,
                functionDescriptionHandle: functionDescriptionHandle);
        }
        */

        private static RfcResultCode HandleGenericFunction(RfcInterop interop, Action<ISapServerConnection, ISapServerFunction> action, IntPtr connectionHandle, IntPtr functionHandle, out RfcErrorInfo errorInfo)
        {
            IntPtr functionDesc = interop.DescribeFunction(
                rfcHandle: functionHandle,
                errorInfo: out RfcErrorInfo functionDescErrorInfo);

            if (functionDescErrorInfo.Code != RfcResultCode.RFC_OK)
            {
                errorInfo = functionDescErrorInfo;
                return functionDescErrorInfo.Code;
            }

            var connection = new SapServerConnection(interop, connectionHandle);
            var function = new SapServerFunction(interop, functionHandle, functionDesc);

            try
            {
                action(connection, function);

                errorInfo = default;
                return RfcResultCode.RFC_OK;
            }
            catch (Exception ex)
            {
                errorInfo = new RfcErrorInfo
                {
                    Code = RfcResultCode.RFC_EXTERNAL_FAILURE,
                    Message = ex.Message,
                };
                return RfcResultCode.RFC_EXTERNAL_FAILURE;
            }
        }

        private static RfcResultCode HandleGenericMetadata(RfcInterop interop, SapConnectionParameters parameters, string functionName, out IntPtr funcDescHandle)
        {
            RfcConnectionParameter[] interopParameters = parameters.ToInterop();

            IntPtr connection = interop.OpenConnection(
                connectionParams: interopParameters,
                paramCount: (uint)interopParameters.Length,
                errorInfo: out RfcErrorInfo connectionErrorInfo);

            if (connectionErrorInfo.Code != RfcResultCode.RFC_OK)
            {
                funcDescHandle = IntPtr.Zero;
                return connectionErrorInfo.Code;
            }

            funcDescHandle = interop.GetFunctionDesc(
                rfcHandle: connection,
                funcName: functionName,
                errorInfo: out RfcErrorInfo errorInfo);

            interop.CloseConnection(
                rfcHandle: connection,
                errorInfo: out RfcErrorInfo _);

            return errorInfo.Code;
        }
    }
}
