using System;
using System.Collections.Generic;
using System.Linq;
using IRobotLANClient.Enums;

namespace IRobotLANClient.Exceptions;

public class RobotConnectionException : Exception {
	public readonly ConnectionError ConnectionError;

	public string FriendlyMessage {
		get {
			switch (ConnectionError) {
				case ConnectionError.ConnectionRefused:
					// This is assuming that you've already verified this is actually a robot
					return "Another app is already connected";
					
				case ConnectionError.IncorrectCredentials:
					// This is assuming that you've already verified the blid
					return "Incorrect password";
					
				case ConnectionError.ConnectionTimedOut:
					return "Connection timed out";

				case ConnectionError.UnspecifiedError:
				default:
					return $"Unspecified error ({Message})";
			}
		}
	}

	public string RecursiveMessage {
		get {
			List<string> messages = new List<string>();
			for (Exception ex = this; ex != null; ex = ex.InnerException) {
				messages.Add(ex.Message);
			}

			return string.Join(" / ", messages);
		}
	}

	public RobotConnectionException(string message, ConnectionError connectionError, Exception innerException)
		: base(message, innerException)
	{
		ConnectionError = connectionError;
	}
}