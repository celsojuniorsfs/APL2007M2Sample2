using System.Device.Gpio;
using System.Device.I2c;
using Iot.Device.Bmxx80;
using Iot.Device.Bmxx80.ReadResult;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using System.Text;

namespace CheeseCaveDotnet;

class Device
{
    // GPIO pin number for the fan
    private static readonly int s_pin = 21;
    private static GpioController s_gpio;
    private static I2cDevice s_i2cDevice;
    private static Bme280 s_bme280;

    // Acceptable range above or below the desired temperature, in degrees Fahrenheit
    const double DesiredTempLimit = 5;
    // Acceptable range above or below the desired humidity, in percentages
    const double DesiredHumidityLimit = 10;
    // Interval at which telemetry is sent to the cloud, in milliseconds
    const int IntervalInMilliseconds = 5000;

    private static DeviceClient s_deviceClient;
    private static stateEnum s_fanState = stateEnum.off;

    // Device connection string for Azure IoT Hub
    private static readonly string s_deviceConnectionString = "YOUR DEVICE CONNECTION STRING HERE";

    // Enumeration for fan states
    enum stateEnum
    {
        off,
        on,
        failed
    }

    // Main entry point of the application
    private static void Main(string[] args)
    {
        // Initialize GPIO controller and open the pin
        s_gpio = new GpioController();
        s_gpio.OpenPin(s_pin, PinMode.Output);

        // Initialize I2C device and BME280 sensor
        var i2cSettings = new I2cConnectionSettings(1, Bme280.DefaultI2cAddress);
        s_i2cDevice = I2cDevice.Create(i2cSettings);
        s_bme280 = new Bme280(s_i2cDevice);

        // Display startup message
        ColorMessage("Cheese Cave device app.\n", ConsoleColor.Yellow);

        // Initialize device client for Azure IoT Hub
        s_deviceClient = DeviceClient.CreateFromConnectionString(s_deviceConnectionString, TransportType.Mqtt);

        // Set method handler for direct method invocation
        s_deviceClient.SetMethodHandlerAsync("SetFanState", SetFanState, null).Wait();

        // Start monitoring conditions and updating the twin
        MonitorConditionsAndUpdateTwinAsync();

        // Wait for user input to exit
        Console.ReadLine();
        s_gpio.ClosePin(s_pin);
    }

    // Monitor sensor conditions and update the device twin
    private static async void MonitorConditionsAndUpdateTwinAsync()
    {
        while (true)
        {
            // Read sensor data
            Bme280ReadResult sensorOutput = s_bme280.Read();

            // Update the device twin with the sensor data
            await UpdateTwin(
                sensorOutput.Temperature.Value.DegreesFahrenheit,
                sensorOutput.Humidity.Value.Percent
            );

            // Wait for the specified interval before the next read
            await Task.Delay(IntervalInMilliseconds);
        }
    }

    // Handle direct method invocation to set the fan state
    private static Task<MethodResponse> SetFanState(MethodRequest methodRequest, object userContext)
    {
        if (s_fanState is stateEnum.failed)
        {
            string result = "{\"result\":\"Fan failed\"}";
            RedMessage("Direct method failed: " + result);
            return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(result), 400));
        }
        else
        {
            try
            {
                var data = Encoding.UTF8.GetString(methodRequest.Data);
                data = data.Replace("\"", "");

                // Parse and set the fan state
                s_fanState = (stateEnum)Enum.Parse(typeof(stateEnum), data);
                GreenMessage("Fan set to: " + data);

                // Control the GPIO pin based on the fan state
                s_gpio.Write(s_pin, s_fanState == stateEnum.on ? PinValue.High : PinValue.Low);

                string result = "{\"result\":\"Executed direct method: " + methodRequest.Name + "\"}";
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(result), 200));
            }
            catch
            {
                string result = "{\"result\":\"Invalid parameter\"}";
                RedMessage("Direct method failed: " + result);
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(result), 400));
            }
        }
    }

    // Update the device twin with the current temperature and humidity
    private static async Task UpdateTwin(double currentTemperature, double currentHumidity)
    {
        var reportedProperties = new TwinCollection();
        reportedProperties["fanstate"] = s_fanState.ToString();
        reportedProperties["humidity"] = Math.Round(currentHumidity, 2);
        reportedProperties["temperature"] = Math.Round(currentTemperature, 2);
        await s_deviceClient.UpdateReportedPropertiesAsync(reportedProperties);

        GreenMessage("Twin state reported: " + reportedProperties.ToJson());
    }

    // Display a message in the specified console color
    private static void ColorMessage(string text, ConsoleColor clr)
    {
        Console.ForegroundColor = clr;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    // Display a message in green color
    private static void GreenMessage(string text) =>
        ColorMessage(text, ConsoleColor.Green);

    // Display a message in red color
    private static void RedMessage(string text) =>
        ColorMessage(text, ConsoleColor.Red);
}