using System;
using System.Net;
using HttpRestClient.Crm;
using HttpRestClient.Crm.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Action = HttpRestClient.Crm.Models.Action;
//
namespace HttpRestClient.Tests
{
    [TestClass]
    public class WebApiClientTests
    {
        private static WebApiClient client;

        public WebApiClientTests()
        {
            //User credentials
            client = new WebApiClient("https://magtestsystem.crm6.dynamics.com/",
                                       new NetworkCredential("admin@magtestsystem.onmicrosoft.com", "m4gt35t5y5t3m#"),
                                       6);

            //Client credentials
            //client = new WebApiClient("4e8827d1-ff2d-450c-a417-93393ae4a71d", 
            //    "e010773d-2a16-407c-a3e1-a780b5240b15", 
            //    "KReN1o3EBg8cSoygO7PL+a8HNEE5VNZNT1cNn2Rhh/A=", 
            //    "https://magtestsystem.crm6.dynamics.com/");

            client.BaseUrl = "https://magtestsystem.api.crm6.dynamics.com/api/data/v9.0/";

            client.SetupEntitySetNames();
        }

        [ClassCleanup()]
        public static void Cleanup()
        {
            if (client != null)
                client.Dispose();
        }

        [TestMethod]
        public void RetrieveMultipleAccounts()
        {
            EntityCollection results = client.RetrieveMultipleAsync("accounts?$top=10").Result;
            Assert.AreEqual(true, results.IsSuccessStatusCode);
            if (results.IsSuccessStatusCode)
            {
                Assert.AreEqual(10, results.Entities.Count);

                Entity account = results.Entities[0];

                string name = account.Get<string>("name");
                Guid id = account.Get<Guid>("accountid");
                int rating = account.Get<int>("accountratingcode");
                DateTime createdOn = account.Get<DateTime>("createdon");
                bool followEmail = account.Get<bool>("followemail");
            }
        }

        [TestMethod]
        public void RetrieveAccount()
        {
            
            ColumnSet columns = new ColumnSet("name", "accountratingcode", "createdon", "_parentaccountid_value");

            Entity account = new Entity("account")
            {
                ["name"] = "JT Test Retrieve",
                ["parentaccountid"] = new EntityReference("account", new Guid("ACA19CDD-88DF-E311-B8E5-6C3BE5A8B200"))
            };
            Guid id = client.Create(account);

            Entity retrieveAccount = client.RetrieveAsync("account", id, columns).Result;
        }

        [TestMethod]
        public void CreateAndDeleteAccount()
        {
            Entity account = new Entity("account")
            {
                ["name"] = "JT Create Test"
            };
            Guid id = client.Create(account);

            client.Delete("account", id);
        }

        [TestMethod]
        public void SetProcess()
        {
            Action action = new Action("SetProcess")
            {
                ["Target"] = new EntityReference("incident", new Guid("00868FC6-1D67-E811-A95C-000D3AD1C598")),
                ["NewProcess"] = new EntityReference("workflow", new Guid("0FFBCDE4-61C1-4355-AA89-AA1D7B2B8792")),
                ["NewProcessInstance"] = new EntityReference("phonetocaseprocess", new Guid("D0B3D3CC-2367-E811-A962-000D3AD1C0D2"))
            };
            client.Execute(action);
            
        }

        [TestMethod]
        public void UpdateAccount()
        {
            ColumnSet columns = new ColumnSet("name", "accountratingcode", "createdon");

            Entity newAccount = new Entity("account")
            {
                ["name"] = "JT Test Update 1"
            };
            Guid newId = client.Create(newAccount);

            Entity updateAccount = new Entity("account", newId)
            {
                ["name"] = "JT Test Update 2"
            };

            newAccount = client.Update(updateAccount, columns);
        }
    }
}
