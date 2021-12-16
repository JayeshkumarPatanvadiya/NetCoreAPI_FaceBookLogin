using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCoreAPI_FaceBookLogin_BackEnd.Data;
using NetCoreAPI_FaceBookLogin_BackEnd.Helpers;
using NetCoreAPI_FaceBookLogin_BackEnd.Helpers.Interface;
using NetCoreAPI_FaceBookLogin_BackEnd.Model;
using NetCoreAPI_FaceBookLogin_BackEnd.ViewModal;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NetCoreAPI_FaceBookLogin_BackEnd.Controllers
{
    [Route("api/[controller]")]
    public class AccountsController : ControllerBase
    {
        private readonly ApplicationDbContext _appDbContext;
        private readonly UserManager<AppUser> _userManager;
        private readonly IMapper _mapper;
        private readonly IJwtFactory _jwtFactory;
        private readonly JwtIssuerOptions _jwtOptions;
        private static readonly HttpClient Client = new HttpClient();
        private readonly FacebookAuthSettings _fbAuthSettings;
        private readonly ILogger _logger;
        private readonly MailSettings _mailSettings;
        private readonly IConfiguration _config;


        public AccountsController(IConfiguration config, IOptions<MailSettings> mailSettings, ILogger<AccountsController> logger, IOptions<FacebookAuthSettings> fbAuthSettingsAccessor, UserManager<AppUser> userManager, IJwtFactory jwtFactory, IOptions<JwtIssuerOptions> jwtOptions, IMapper mapper, ApplicationDbContext appDbContext)
        {
            _config = config;
            _userManager = userManager;
            _mapper = mapper;
            _appDbContext = appDbContext;
            _jwtFactory = jwtFactory;
            _jwtOptions = jwtOptions.Value;
            _fbAuthSettings = fbAuthSettingsAccessor.Value;
            _logger = logger;
            _mailSettings = mailSettings.Value;

        }
        // POST api/accounts
        [HttpPost("register")]
        public async Task<IActionResult> Post([FromBody] RegistrationViewModel model)
        {
            var userExists = await _userManager.FindByNameAsync(model.Email);
            if (userExists != null)
                return StatusCode(StatusCodes.Status500InternalServerError, new BadHttpRequestException("User Already exists"));

            try
            {
                string address = "";
                WebRequest request = WebRequest.Create(_config.GetValue<string>("PublicIpWebsite"));
                using (WebResponse response = request.GetResponse())
                using (StreamReader stream = new StreamReader(response.GetResponseStream()))
                {
                    address = stream.ReadToEnd();
                }

                int first = address.IndexOf("Address: ") + 9;
                int last = address.LastIndexOf("</body>");
                address = address.Substring(first, last - first);

                IpInfo ipInfo = new IpInfo();
                try
                {
                    string ip = address;
                    string info = new WebClient().DownloadString(_config.GetValue<string>("IPLocation") + ip);
                    ipInfo = JsonConvert.DeserializeObject<IpInfo>(info);
                    RegionInfo myRI1 = new RegionInfo(ipInfo.Country);
                    ipInfo.Country = myRI1.EnglishName;
                    model.Location = ipInfo.City;
                }
                catch (Exception ex)
                {
                    return new BadRequestObjectResult(ex.Message);
                }

                var userIdentity = _mapper.Map<AppUser>(model);
                _logger.LogWarning("User account." + userIdentity);
                var result = await _userManager.CreateAsync(userIdentity, model.Password);

                if (!result.Succeeded) return new BadRequestObjectResult(Errors.AddErrorsToModelState(result, ModelState));

                //send confirmation email

                var token = await _userManager.GenerateEmailConfirmationTokenAsync(userIdentity);
                var param = new Dictionary<string, string>
    {
        {"token", token },
        {"email", userIdentity.Email }
    };

                var callback = QueryHelpers.AddQueryString(_config.GetValue<string>("FrontEndURL"), param);
                //await _userManager.AddToRoleAsync(userIdentity, "Viewer");
                await SendEmailAsync(callback, userIdentity.Email);
                await _appDbContext.Customers.AddAsync(new Customer { IdentityId = userIdentity.Id, Location = model.Location });
                await _appDbContext.SaveChangesAsync();
                return new OkObjectResult("Account created");
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex.Message);
            }
        }

        public Task SendEmailAsync(string callback, string email)
        {
            #region Send Email

            var fromMail = new MailAddress(_mailSettings.Mail, _mailSettings.DisplayName); // set your email    
            var fromEmailpassword = _mailSettings.Password; // Set your password     
            var toEmail = new MailAddress(email);
            var smtp = new SmtpClient();
            smtp.Host = _mailSettings.Host;
            smtp.Port = _mailSettings.Port;
            smtp.EnableSsl = true;
            smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
            smtp.UseDefaultCredentials = false;
            smtp.Credentials = new NetworkCredential(fromMail.Address, fromEmailpassword);

            var Message = new MailMessage(fromMail, toEmail);
            // Add a carbon copy recipient.
            Message.Subject = _config.GetValue<string>("EmailConfirmSubject");
            Message.Body = callback;
            Message.IsBodyHtml = true;
            smtp.Send(Message);
            return Task.FromResult<object>(null);
            #endregion


        }

        [HttpPost("EmailConfirmation")]
        public async Task<IActionResult> EmailConfirmation([FromQuery] string email, [FromQuery] string token)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return BadRequest("Invalid Email Confirmation Request");
            var confirmResult = await _userManager.ConfirmEmailAsync(user, token);
            if (!confirmResult.Succeeded)
                return BadRequest("Invalid Email Confirmation Request");
            return Ok();
        }

        [HttpPost("login")]
        public async Task<IActionResult> Post([FromBody] CredentialsViewModel credentials)
        {

            var identity = await GetClaimsIdentity(credentials.Email, credentials.Password);
            if (identity == null)
            {
                return BadRequest(Errors.AddErrorToModelState("login_failure", "Invalid username or password.", ModelState));
            }

            var jwt = await Tokens.GenerateJwt(identity, _jwtFactory, credentials.Email, _jwtOptions, new JsonSerializerSettings { Formatting = Formatting.Indented });
            return new OkObjectResult(jwt);
        }

        private async Task<ClaimsIdentity> GetClaimsIdentity(string userName, string password)
        {
            if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
                return await Task.FromResult<ClaimsIdentity>(null);

            // get the user to verifty
            var userToVerify = await _userManager.FindByNameAsync(userName);
            if (userToVerify == null) return await Task.FromResult<ClaimsIdentity>(null);

            var emailConfirmUser = await _userManager.IsEmailConfirmedAsync(userToVerify);
            if (emailConfirmUser == false) return await Task.FromResult<ClaimsIdentity>(null);

            // check the credentials
            if (await _userManager.CheckPasswordAsync(userToVerify, password))
            {
                return await Task.FromResult(_jwtFactory.GenerateClaimsIdentity(userName, userToVerify.Id));
            }

            // Credentials are invalid, or account doesn't exist
            return await Task.FromResult<ClaimsIdentity>(null);
        }

        [HttpPost("facebook-login")]
        public async Task<IActionResult> Facebook([FromBody] FacebookAuthViewModel model)
        {
            // 1.generate an app access token
            var test = _fbAuthSettings.AppId;
            var test2 = _fbAuthSettings.AppSecret;

            var appAccessTokenResponse = await Client.GetStringAsync($"https://graph.facebook.com/oauth/access_token?client_id={_fbAuthSettings.AppId}&client_secret={_fbAuthSettings.AppSecret}&grant_type=client_credentials");
            var appAccessToken = JsonConvert.DeserializeObject<FacebookAppAccessToken>(appAccessTokenResponse);
            // 2. validate the user access token
            var userAccessTokenValidationResponse = await Client.GetStringAsync($"https://graph.facebook.com/debug_token?input_token={model.AccessToken}&access_token={appAccessToken.AccessToken}");
            var userAccessTokenValidation = JsonConvert.DeserializeObject<FacebookUserAccessTokenValidation>(userAccessTokenValidationResponse);

            if (!userAccessTokenValidation.Data.IsValid)
            {
                return BadRequest(Errors.AddErrorToModelState("login_failure", "Invalid facebook token.", ModelState));
            }

            // 3. we've got a valid token so we can request user data from fb
            var userInfoResponse = await Client.GetStringAsync($"https://graph.facebook.com/v2.8/me?fields=id,email,first_name,last_name,name,gender,locale,birthday,picture&access_token={model.AccessToken}");
            var userInfo = JsonConvert.DeserializeObject<FacebookUserData>(userInfoResponse);

            // 4. ready to create the local user account (if necessary) and jwt
            var user = await _userManager.FindByEmailAsync(userInfo.Email);

            if (user == null)
            {
                var appUser = new AppUser
                {
                    FirstName = userInfo.FirstName,
                    LastName = userInfo.LastName,
                    FacebookId = userInfo.Id,
                    Email = userInfo.Email,
                    UserName = userInfo.Email,
                    PictureUrl = userInfo.Picture.Data.Url
                };

                var result = await _userManager.CreateAsync(appUser, Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Substring(0, 8));

                if (!result.Succeeded) return new BadRequestObjectResult(Errors.AddErrorsToModelState(result, ModelState));

                await _appDbContext.Customers.AddAsync(new Customer { IdentityId = appUser.Id, Location = "", Locale = userInfo.Locale, Gender = userInfo.Gender });
                await _appDbContext.SaveChangesAsync();
            }

            // generate the jwt for the local user...
            var localUser = await _userManager.FindByNameAsync(userInfo.Email);

            if (localUser == null)
            {
                return BadRequest(Errors.AddErrorToModelState("login_failure", "Failed to create local user account.", ModelState));
            }

            var jwt = await Tokens.GenerateJwt(_jwtFactory.GenerateClaimsIdentity(localUser.UserName, localUser.Id),
              _jwtFactory, localUser.UserName, _jwtOptions, new JsonSerializerSettings { Formatting = Formatting.Indented });

            return new OkObjectResult(jwt);
        }

        [HttpGet]
        public async Task<IActionResult> GetListOfUsers()
        {
            var users = await _userManager.Users.ToListAsync();
            return new OkObjectResult(users);
        }
    }
}
