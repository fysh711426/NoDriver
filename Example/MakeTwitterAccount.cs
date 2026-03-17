using NoDriver.Core.Runtime;

namespace Example
{
    public class MakeTwitterAccount
    {
        private static readonly string[] _months = {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        };

        private static readonly Random _random = new Random();

        public static async Task Main()
        {
            await using (var browser = await Browser.CreateAsync())
            {
                var tab = await browser.GetAsync("https://x.com/?lang=en");

                Console.WriteLine(@"Finding the ""create account"" button.");
                var createAccount = await tab.FindAsync("create account", bestMatch: true);
                if (createAccount == null)
                    throw new InvalidOperationException("CreateAccount button is null.");

                Console.WriteLine(@"""create account"" => click");
                await createAccount.ClickAsync();

                Console.WriteLine("Finding the email input field.");
                var email = await tab.SelectAsync("input[type=email]");
                if (email == null)
                {
                    var useMailInstead = await tab.FindAsync("use email instead");
                    if (useMailInstead == null)
                        throw new InvalidOperationException("UseMailInstead button is null.");

                    await useMailInstead.ClickAsync();
                    email = await tab.SelectAsync("input[type=email]");
                }
                if (email == null)
                    throw new InvalidOperationException("Email input is null.");

                string randStr(int length)
                {
                    const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
                    return new string(Enumerable.Repeat(chars, length).Select(s => s[_random.Next(s.Length)]).ToArray());
                }

                Console.WriteLine(@"Filling in the ""email"" input field.");
                await email.SendKeysAsync($"{randStr(8)}@{randStr(8)}.com");

                Console.WriteLine("Finding the name input field.");
                var name = await tab.SelectAsync("input[type=text]");
                if (name == null)
                    throw new InvalidOperationException("Name input is null.");

                Console.WriteLine(@"Filling in the ""name"" input field.");
                await name.SendKeysAsync(randStr(8));

                Console.WriteLine(@"Finding the ""month"" , ""day"" and ""year"" fields in 1 go.");

                var sels = await tab.SelectAllAsync("select");
                if (sels.Count < 3)
                    throw new InvalidOperationException("Month, Day or Year select is null.");

                var selMonth = sels[0];
                var selDay = sels[1];
                var selYear = sels[2];

                Console.WriteLine(@"Filling in the ""month"" input field.");
                await selMonth.SendKeysAsync(_months[_random.Next(0, 12)]);

                Console.WriteLine(@"Filling in the ""day"" input field.");
                await selDay.SendKeysAsync($"{_random.Next(1, 29)}");

                Console.WriteLine(@"Filling in the ""year"" input field.");
                await selYear.SendKeysAsync($"{_random.Next(1980, 2006)}");

                await tab.WaitAsync();

                var cookieBarAccept = await tab.FindAsync("accept all", bestMatch: true);
                if (cookieBarAccept != null)
                    await cookieBarAccept.ClickAsync();

                await tab.WaitAsync(1);

                var nextBtn = await tab.FindAsync("next", bestMatch: true);
                if (nextBtn == null)
                    throw new InvalidOperationException("Next button is null.");

                await nextBtn.MouseClickAsync();

                Console.WriteLine("Sleeping 2 seconds.");
                await tab.WaitAsync(2);

                Console.WriteLine(@"Finding ""next"" button.");
                nextBtn = await tab.FindAsync("next", bestMatch: true);
                if (nextBtn == null)
                    throw new InvalidOperationException("Next button is null.");

                Console.WriteLine(@"Clicking ""next"" button.");
                await nextBtn.MouseClickAsync();

                await tab.SelectAsync("[role=button]");

                Console.WriteLine(@"Finding ""sign up""  button.");
                var signUpBtn = await tab.FindAsync("Sign up", bestMatch: true);
                if (signUpBtn == null)
                    throw new InvalidOperationException("SignUp button is null.");

                Console.WriteLine(@"Clicking ""sign up""  button.");
                await signUpBtn.ClickAsync();

                Console.WriteLine(@"The rest of the ""implementation"" is out of scope.");
                await tab.WaitAsync(10);
            }
        }
    }
}
