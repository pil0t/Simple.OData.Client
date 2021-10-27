using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using Microsoft.OData.Edm.Validation;
using Simple.OData.Client;
using TripPin;

namespace ConsoleApp8
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var httpClient = new HttpClient();
            ODataClient ws = new ODataClient(new ODataClientSettings(httpClient) { BaseUri = new Uri("https://services.odata.org/TripPinRESTierService/") });

            var odc = new TripPinRESTierService(ws);

            var airportInfo = await odc.GetNearestAirport(0, 0);
            var al = await odc.People.FindEntriesAsync();
            foreach (var airline in al)
            {
                Console.WriteLine(airline.FirstName + " " + airline.LastName);
            }
        }
    }
}
