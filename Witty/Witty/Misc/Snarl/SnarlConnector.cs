using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;

namespace Snarl
{
	public static class SnarlConnector
	{
		#region Interop Imports

		[DllImport( "user32.dll", SetLastError = true )]
		private static extern IntPtr FindWindow( string lpClassName, string lpWindowName );

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr FindWindowEx( IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow );

		[DllImport( "user32.dll", SetLastError = true, CharSet = CharSet.Auto )]
		private static extern uint RegisterWindowMessage( string lpString );

		[DllImport( "user32.dll", CharSet = CharSet.Auto, SetLastError = true )]
		private static extern IntPtr SendMessage( IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam );

		[DllImport( "user32.dll", SetLastError = true, CharSet = CharSet.Auto )]
		private static extern IntPtr SendMessageTimeout( IntPtr windowHandle, uint Msg, IntPtr wParam, IntPtr lParam, SendMessageTimeoutFlags flags, uint timeout, out IntPtr result );

		[DllImport( "user32.dll", CharSet = CharSet.Auto )]
		public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, Int32 nMaxCount);

		[DllImport("user32.dll")]
		private static extern bool IsWindow(IntPtr hWnd);

		[Flags]
		private enum SendMessageTimeoutFlags: uint
		{
			SMTO_NORMAL = 0x0000,
			SMTO_BLOCK = 0x0001,
			SMTO_ABORTIFHUNG = 0x0002,
			SMTO_NOTIMEOUTIFNOTHUNG = 0x0008
		}

		[StructLayout( LayoutKind.Sequential )]
		private struct COPYDATASTRUCT
		{
			public IntPtr dwData;   // DWORD - Needs to IntPtr and not Int32 for some reason to work on Vista 64. Different versions of Win32 SDK might say different things
			public Int32 cbData;    // DWORD
			public IntPtr lpData;   // PVOID
		}

		#endregion


		#region Private declarations
		
		private const string SNARL_WINDOW_TITLE = "Snarl";
		
		private const uint WM_SNARLTEST = (uint)WindowsMessage.WM_USER + 237;

		private static IntPtr _snarlWindow = IntPtr.Zero;
		private static UInt32 _snarlGlobalMessage = 0;
		private const string SNARL_GLOBAL_MSG = "SnarlGlobalEvent";

		#endregion

		public const int SNARL_STRING_LENGTH = 1024; 


		/* Global event identifiers
		    Identifiers marked with a '*' are sent by Snarl in two ways:
		    1. As a broadcast message (uMsg = 'SNARL_GLOBAL_MSG')
		    2. To the window registered in snRegisterConfig() or snRegisterConfig2()
		       (uMsg = reply message specified at the time of registering)
		    
		    In both cases these values appear in wParam.
		    
		    Identifiers not marked are not broadcast; they are simply sent to the application's registered window.
		*/
		public const Int32 SNARL_LAUNCHED = 1;        // Snarl has just started running*
		public const Int32 SNARL_QUIT = 2;            // Snarl is about to stop running*
		public const Int32 SNARL_ASK_APPLET_VER = 3;  // (R1.5) Reserved for future use
		public const Int32 SNARL_SHOW_APP_UI = 4;     // (R1.6) Application should show its UI


		/* Message event identifiers
		    These are sent by Snarl to the window specified in snShowMessage() when the
		    Snarl Notification raised times out or the user clicks on it.
		*/
		public const Int32 SNARL_NOTIFICATION_CLICKED = 32;     // notification was right-clicked by user
		public const Int32 SNARL_NOTIFICATION_TIMED_OUT = 33;   //
		public const Int32 SNARL_NOTIFICATION_ACK = 34 ;        // notification was left-clicked by user

		public const Int32 SNARL_NOTIFICATION_CANCELLED = SNARL_NOTIFICATION_CLICKED; // Added in V37 (R1.6) -- same value, just improved the meaning of it


		// --------------------------------------------------------------------------

		/// <summary>
		/// Returns a handle to the current Snarl Dispatcher window, or zero if it wasn't found.
		/// </summary>
		/// <returns>Handle to the current Snarl Dispatcher, zero on failure.</returns>
		public static IntPtr GetSnarlWindow()
		{
			return GetSnarlWindow(false);
		}

		/// <summary>
		/// Returns a handle to the current Snarl Dispatcher window, or zero if it wasn't found.
		/// </summary>
		/// <param name="forceUpdate">Set to true to get a non cached window handle. (Use when receiving SNARL_LAUNCHED message)</param>
		/// <returns>Handle to the current Snarl Dispatcher, zero on failure.</returns>
		public static IntPtr GetSnarlWindow(bool forceUpdate)
		{
			if (forceUpdate || _snarlWindow == IntPtr.Zero)
				_snarlWindow = FindWindow(null, "Snarl");

			return _snarlWindow;
		}
		
		/// <summary>
		/// Gets the Snarl global message.
		/// </summary>
		/// <returns>Snarl global ATOM. Zero on failure.</returns>
		public static Int32 GetGlobalMsg()
		{
			if ( _snarlGlobalMessage == 0 )
				_snarlGlobalMessage = RegisterWindowMessage( SNARL_GLOBAL_MSG );

			return (Int32) _snarlGlobalMessage;
		}

		/// <summary>
		/// Checks if Snarl is currently running and, if it is, retrieves the major and minor version numbers.
		/// Depricated, use GetVersionEx() instead.
		/// </summary>
		/// <param name="major">The major version number.</param>
		/// <param name="minor">The minor version number.</param>
		/// <returns><c>true</c> if Snarl is running, otherwise <c>false</c>.</returns>
		public static bool GetVersion( out UInt16 major, out UInt16 minor )
		{
			major = 0;
			minor = 0;

			SNARLSTRUCT message = new SNARLSTRUCT();
			message.Cmd = (Int16) SNARL_COMMAND.SNARL_GET_VERSION;

			Int32 version = Send( message, IntPtr.Zero );
			if (version > 0)
			{
				major = (UInt16) ( ( version >> 16 ) & 0xFF );
				minor = (UInt16) ( version & 0xFF );
				return true;
			}
			else
				return false;
		}

		/// <summary>
		/// Returns a fully qualified path to Snarl's installation folder.
		/// </summary>
		/// <returns>Path to Snarl's installation folder. Empty string on failure.</returns>
		public static string GetAppPath()
		{
			StringBuilder sb = new StringBuilder(512);

			IntPtr hwnd = (IntPtr) GetSnarlWindow();
			if (hwnd != IntPtr.Zero)
			{
				IntPtr hWndPath = FindWindowEx(hwnd, IntPtr.Zero, "static", null);
				if (hWndPath != IntPtr.Zero)
				{
					GetWindowText(hWndPath, sb, 512);
				}
			}

			return sb.ToString();
		}

		/// <summary>
		/// Returns a fully qualified path to Snarl's default icon location.
		/// </summary>
		/// <returns>Path to Snarl's default icon location. Empty string on failure.</returns>
		public static string GetIconsPath()
		{
			return Path.Combine( GetAppPath(), "etc\\icons" );
		}

		/// <summary>
		/// Gets the Snarl system version number. This represents the system build number 
		/// and can be used to uniquely identify the version of Snarl running.
		/// </summary>
		/// <returns>Snarl system version number. Zero on failure.</returns>
		public static Int32 GetVersionEx()
		{
			SNARLSTRUCT message = new SNARLSTRUCT();
			message.Cmd = (Int16) SNARL_COMMAND.SNARL_GET_VERSION_EX;

			return Send( message, IntPtr.Zero );
		}

		/// <summary>
		/// Determines whether the specified message is still visible.
		/// </summary>
		/// <param name="handle">The handle returned from <see cref="ShowMessage"/> or <see cref="ShowMessageEx"/>.</param>
		/// <returns><c>true</c> if the specified message is still visible; otherwise, <c>false</c>.</returns>
		public static bool IsMessageVisible( Int32 handle )
		{
			if ( handle == 0 )
				return false;

			SNARLSTRUCT message = new SNARLSTRUCT();
			message.Cmd = (Int16) SNARL_COMMAND.SNARL_IS_VISIBLE;
			message.Id = handle;

			return Convert.ToBoolean( Send( message, IntPtr.Zero ) );
		}

		/// <summary>
		/// Displays a Snarl message.
		/// </summary>
		/// <param name="title">The title of the message.</param>
		/// <param name="text">The text of the message.</param>
		/// <param name="timeout">The timeout in seconds. Zero means the message is displayed indefinitely.</param>
		/// <param name="iconPath">The location of a PNG image which will be displayed alongside the message text.</param>
		/// <param name="client">The client handle to send messages back to.</param>
		/// <param name="reply">The windows message to send to the client if the Snarl message is clicked.</param>
		/// <returns><c>Message id</c> on success. <c>M_RESULT</c> on failure.</returns>
		public static Int32 ShowMessage( string title, string text, Int32 timeout, string iconPath, IntPtr client, WindowsMessage reply )
		{
			SNARLSTRUCT message = new SNARLSTRUCT();
			message.Cmd = (Int16) SNARL_COMMAND.SNARL_SHOW;
			message.Title = StringToUtf8(title);
			message.Text = StringToUtf8(text);
			message.Icon = StringToUtf8(iconPath);
			message.LngData2 = client.ToInt32();
			message.Id = (Int32) reply;
			message.Timeout = timeout;

			return Send( message, client );
		}


		/// <summary>
		/// Displays an extended Snarl message.
		/// </summary>
		/// <param name="alert">The alert class registered with Snarl using <see cref="RegisterAlert"/>.</param>
		/// <param name="title">The title of the message.</param>
		/// <param name="text">The text of the message.</param>
		/// <param name="timeout">The timeout in seconds. Zero means the message is displayed indefinitely.</param>
		/// <param name="iconPath">The location of a PNG image which will be displayed alongside the message text.</param>
		/// <param name="client">The client handle.</param>
		/// <param name="reply">The windows message to send to the client if the Snarl message is clicked.</param>
		/// <param name="sound">The location of a sound file which will be played when the message is displayed.</param>
		/// <returns><c>Message id</c> on success. <c>M_RESULT</c> on failure.</returns>
		public static Int32 ShowMessageEx( string alert, string title, string text, Int32 timeout, string iconPath, IntPtr client, WindowsMessage reply, string sound )
		{
			SNARLSTRUCTEX message = new SNARLSTRUCTEX();
			message.Cmd = (Int16) SNARL_COMMAND.SNARL_SHOW_EX;
			message.Class = StringToUtf8(alert);
			message.Title = StringToUtf8(title);
			message.Text = StringToUtf8(text);
			message.Timeout = timeout;
			message.Icon = StringToUtf8(iconPath);
			message.LngData2 = client.ToInt32();
			message.Id = (Int32) reply;
			message.Extra = StringToUtf8(sound);

			return Send(message, client);

			//if ( snarlResult.ToInt32() >= 0 )
			//	return snarlResult.ToInt32();
			//else
			//	throw new SnarlException( ConvertToMResult( snarlResult ) );
		}

		/// <summary>
		/// Hides the specified snarl message.
		/// </summary>
		/// <param name="handle">The handle returned from <see cref="ShowMessage"/> or <see cref="ShowMessageEx"/>.</param>
		/// <returns><c>true</c> if the message was hidden, otherwise <c>false</c>.</returns>
		public static bool HideMessage( Int32 handle )
		{
			SNARLSTRUCT message = new SNARLSTRUCT();
			message.Cmd = (Int16) SNARL_COMMAND.SNARL_HIDE;
			message.Id = handle;

			return Convert.ToBoolean( Send( message, IntPtr.Zero ) );
		}

		/// <summary>
		/// Registers an alert type for the specified application. The application must have been previously registered with <see cref="RegisterConfig"/>.
		/// </summary>
		/// <param name="appName">The name of the application.</param>
		/// <param name="alert">The name of the alert.</param>
		/// <returns><see cref="M_RESULT"/> value indicating the result of the Snarl request.</returns>
		public static M_RESULT RegisterAlert( string appName, string alert )
		{
			SNARLSTRUCT message = new SNARLSTRUCT();
			message.Cmd = (Int16) SNARL_COMMAND.SNARL_REGISTER_ALERT;
			message.Title = StringToUtf8(appName);
			message.Text = StringToUtf8(alert);

			return ConvertToMResult( Send( message, IntPtr.Zero ) );
		}

		/// <summary>
		/// Registers and application configuration interface with Snarl.
		/// </summary>
		/// <param name="client">The client windows handle.</param>
		/// <param name="appName">Name to be displayed in Snarl's "Registered Applications" list. This should match the title of your application.</param>
		/// <param name="reply">Message to send to application configuration interface window is the application name is double clicked in Snarl.</param>
		/// <returns><see cref="M_RESULT"/> value indicating the result of the Snarl request.</returns>
		public static M_RESULT RegisterConfig( IntPtr client, string appName, WindowsMessage reply )
		{
			return RegisterConfig( client, appName, reply, null );
		}

		/// <summary>
		/// Registers and application configuration interface with Snarl.
		/// (Is the same as snRegisterConfig2 from the official API)
		/// </summary>
		/// <param name="client">The client windows handle.</param>
		/// <param name="appName">Name to be displayed in Snarl's "Registered Applications" list. This should match the title of your application.</param>
		/// <param name="reply">Message to send to application configuration interface window is the application name is double clicked in Snarl.</param>
		/// <param name="iconPath">A path to an icon that will be displayed alongside the application name in Snarl.</param>
		/// <returns><see cref="M_RESULT"/> value indicating the result of the Snarl request.</returns>
		public static M_RESULT RegisterConfig( IntPtr client, string appName, WindowsMessage reply, string iconPath )
		{
			SNARLSTRUCT message = new SNARLSTRUCT();
			message.Cmd = (Int16) SNARL_COMMAND.SNARL_REGISTER_CONFIG_WINDOW_2;
			message.LngData2 = client.ToInt32();
			message.Id = (Int32) reply;
			message.Title = StringToUtf8(appName);
			if ( !string.IsNullOrEmpty( iconPath ) )
				message.Icon = StringToUtf8(iconPath);

			return ConvertToMResult ( Send( message, client ) );
		}

		/// <summary>
		/// Removes the application previously registered using <c>client</c>. Typically done as part of the application's shutdown procedure.
		/// </summary>
		/// <param name="client">The client handle used to register the application using <see cref="RegisterConfig"/>.</param>
		/// <returns><c>M_OK</c> if the revocation was successfule.</returns>
		public static M_RESULT RevokeConfig( IntPtr client )
		{
			if ( client == IntPtr.Zero )
				return M_RESULT.M_INVALID_ARGS;

			SNARLSTRUCT message = new SNARLSTRUCT();
			message.Cmd = (Int16) SNARL_COMMAND.SNARL_REVOKE_CONFIG_WINDOW;
			message.LngData2 = client.ToInt32();
			client = IntPtr.Zero;

			return ConvertToMResult ( Send( message, client ) );
		}

		/// <summary>
		/// Sets the timeout of the specified notification to <c>timeout</c> seconds.
		/// </summary>
		/// <param name="handle">The message handle.</param>
		/// <param name="timeout">The timeout in seconds.</param>
		/// <returns><see cref="M_RESULT"/> value indicating the result of the Snarl request.</returns>
		public static M_RESULT SetTimeout( Int32 handle, Int32 timeout )
		{
			SNARLSTRUCT message = new SNARLSTRUCT();
			message.Cmd = (Int16) SNARL_COMMAND.SNARL_SET_TIMEOUT;
			message.Id = handle;
			message.LngData2 = timeout;

			return ConvertToMResult( Send( message, IntPtr.Zero ) );
		}

		/// <summary>
		/// Updates the contents of an existing Snarl message.
		/// </summary>
		/// <param name="handle">The handle of the existing Snarl message.</param>
		/// <param name="title">The new title.</param>
		/// <param name="text">The new text.</param>
		/// <param name="iconPath">The new icon path.</param>
		/// <returns><c>M_OK</c> on success. <see cref="M_RESULT"/> value indicating the result of the Snarl request.</returns>
		public static M_RESULT UpdateMessage(Int32 handle, string title, string text, string iconPath)
		{
			SNARLSTRUCT message = new SNARLSTRUCT();
			message.Cmd = (Int16) SNARL_COMMAND.SNARL_UPDATE;
			message.Id = handle;
			message.Title = StringToUtf8(title);
			message.Text = StringToUtf8(text);
			message.Icon = StringToUtf8(iconPath);

			return ConvertToMResult( Send( message, IntPtr.Zero ) );
		}

		/// <summary>
		/// Requests Snarl to display it's test message.
		/// </summary>
		public static void TestMessage()
		{
			IntPtr outcome;
			SendMessageTimeout( GetSnarlWindow(), (uint) WM_SNARLTEST, IntPtr.Zero, IntPtr.Zero, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 500, out outcome );
		}

		/// <summary>
		/// Test if a message ID returned from SendMessage and SendMessageEx is valid or a M_RESULT value
		/// </summary>
		/// <param name="MessageId">Message ID</param>
		/// <returns><c>true</c> if a valid ID, <c>false</c> if ID is a M_RESULT value.</returns>
		public static bool IsValidMessageId(Int32 nId)
		{
			return ! Convert.ToBoolean( unchecked( nId & (int)0x80000000 ) );
		}



		public static M_RESULT ConvertToMResult(Int32 result)
		{
			return (M_RESULT)((uint)result);
		}

		//////////////////////////////////////////////////////////////////////
		// Private functions
		//////////////////////////////////////////////////////////////////////

		private static Int32 Send<T>( T snarlStruct, IntPtr client )
		{
			IntPtr nSendMessageResult = IntPtr.Zero;
			IntPtr ptrToSnarlStruct = IntPtr.Zero;
			IntPtr ptrToCds = IntPtr.Zero;

			Int32 nReturn = unchecked((int)(uint)M_RESULT.M_FAILED);

			if (!IsWindow(GetSnarlWindow()))
				return nReturn;

			try
			{
				COPYDATASTRUCT cds = new COPYDATASTRUCT();
				cds.dwData = (IntPtr)2;
				cds.cbData = Marshal.SizeOf( typeof(T) );
				
				ptrToSnarlStruct = Marshal.AllocHGlobal( (int)cds.cbData );
				Marshal.StructureToPtr( snarlStruct, ptrToSnarlStruct, false );
				cds.lpData = ptrToSnarlStruct;

				ptrToCds = Marshal.AllocHGlobal( Marshal.SizeOf( typeof( COPYDATASTRUCT ) ) );
				Marshal.StructureToPtr( cds, ptrToCds, false );

				if (SendMessageTimeout(GetSnarlWindow(),
						  (uint)WindowsMessage.WM_COPYDATA,
						  client,
						  ptrToCds,
						  SendMessageTimeoutFlags.SMTO_ABORTIFHUNG | SendMessageTimeoutFlags.SMTO_NOTIMEOUTIFNOTHUNG,
						  1000,
						  out nSendMessageResult) == IntPtr.Zero)
				{
					//  If GetLastError returns 0, SendMessageTimeout timed out. Else we return the default M_RESULT.FAILED
					Int32 nLastError = Marshal.GetLastWin32Error();
					if (nLastError == 0)
						nReturn = unchecked((int)(uint)M_RESULT.M_TIMED_OUT);
				}
				else
				{
					nReturn = unchecked((Int32)nSendMessageResult.ToInt64());
				}
			}
			finally
			{
				Marshal.FreeHGlobal( ptrToCds );
				Marshal.FreeHGlobal( ptrToSnarlStruct );
			}

			return nReturn;
		}

		/// <summary>
		/// Use this function to convert a string into an UTF8 encoded byte[]
		/// </summary>
		/// <param name="strToConvert">The managed string object to convert.</param>
		/// <returns><c>byte[]</c> with the converted string.</returns>
		private static byte[] StringToUtf8(string strToConvert)
		{
			byte[] returnString = new byte[SNARL_STRING_LENGTH];

			UTF8Encoding utf8 = new UTF8Encoding();
			utf8.GetBytes(strToConvert, 0, strToConvert.Length, returnString, 0);

			return returnString;
		}
	}
}
