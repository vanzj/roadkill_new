using AutoMapper;
using Roadkill.Core.Authorization;

namespace Roadkill.Api.Common.Request
{
	public class UserRequest
	{
		public string Email { get; set; }

		public string Password { get; set; }
	}
}