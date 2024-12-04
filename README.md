---
page_type: sample
languages:
- azdeveloper
- csharp
- bicep
products:
- azure
- azure-container-apps
- azure-service-bus
- azure-iot-hub
urlFragment: pubsub-dapr-csharp-servicebus
name: Microservice communication using pubsub (async)(C#) and iot hub (AMQP)
description: Create a subscriber microservice with C# to demonstrate how Dapr enables a subcribe pattern. Console app will publish a message on service bus topic, subscriber microservice will pick it up and execute the job. Both services talk to each other using AMQP via Azure IoT Hub.
---
<!-- YAML front-matter schema: https://review.learn.microsoft.com/en-us/help/contribute/samples/process/onboarding?branch=main#supported-metadata-fields-for-readmemd -->

# Microservice communication using pubsub (async) and AMQP (IoT Hub)

![](images/pubsub-diagram.png)

In this quickstart, you'll create a subscriber microservice to demonstrate how Dapr enables a publish-subcribe pattern. The publisher will be a console app (`console scheduler`) that schedules a job on a specific topic, while subscriber (`job solver`) will listen for messages of specific topics and execute the job. See [Why Pub-Sub](#why-pub-sub) to understand when this pattern might be a good choice for your software architecture.

The key thing about this sample is that both apps console scheduler app are registered as IoT devices and send telemetry to the hub (IoT Manager). Manager app is responsible for routing events back to devices. 

All `scheduler` telemetry (device-to-cloud) is transformed to cloud-to-device messages and send back to `solver`.

This is not a most common use of IoT hub, but it demostrates the messaging capabilites.

## Dapr

For more details about this quickstart example please see the [Pub-Sub Quickstart documentation](https://docs.dapr.io/getting-started/quickstarts/pubsub-quickstart/).

Visit [this](https://docs.dapr.io/developing-applications/building-blocks/pubsub/) link for more information about Dapr and Pub-Sub.

> **Note:** This example leverages the Dapr client SDK.  If you are looking for the example using only HTTP [click here](../http).

This quickstart includes one publisher - `console-scheduler`

- Dotnet client console app `console-scheduler` 

And one subscriber: 
 
- Dotnet job-solver `job-solver`

There's also Azure IoT Hub server app - `iot-manager`, it handles events from IoT Hub and includes `/negotiate` API endpoint that registeres IoT Devices and returns connection string for them.

### Pre-requisites

For this example, you will need:

- [Dapr CLI](https://docs.dapr.io/getting-started)
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
<!-- IGNORE_LINKS -->
- [Docker Desktop](https://www.docker.com/products/docker-desktop)
<!-- END_IGNORE -->

### Deploy apps to Azure (Azure Container Apps, Azure Service Bus)

#### Deploy to Azure for dev-test

NOTE: make sure you have Azure Dev CLI pre-reqs [here](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/install-azd?tabs=winget-windows%2Cbrew-mac%2Cscript-linux&pivots=os-windows) and are on version 0.9.0-beta.3 or greater.

5. Run the following command to initialize the project. 

```bash
azd init --template https://github.com/karpikpl/azure-iot-hub-sample
``` 

This command will clone the code to your current folder and prompt you for the following information:

- `Environment Name`: This will be used as a prefix for the resource group that will be created to hold all Azure resources. This name should be unique within your Azure subscription.

6. Run the following command to package a deployable copy of your application, provision the template's infrastructure to Azure and also deploy the application code to those newly provisioned resources.

```bash
azd up
```

This command will prompt you for the following information:
- `Azure Location`: The Azure location where your resources will be deployed.
- `Azure Subscription`: The Azure Subscription where your resources will be deployed.

> NOTE: This may take a while to complete as it executes three commands: `azd package` (packages a deployable copy of your application),`azd provision` (provisions Azure resources), and `azd deploy` (deploys application code). You will see a progress indicator as it packages, provisions and deploys your application.

#### Azure Role Assignments

Deployment will attempt to create role assignments for the resources, as well as the indetity executing the deployment. In scenarios where `Subscription Owner` role is not available, following role assignments have to be added manually by the admin:

* `Azure Service Bus Data Sender`
* `Azure Service Bus Data Receiver`
* `Azure Container Registry Pull`
* `IoT Hub Data Contributor`
* `IoT Hub Registry Contributor`

Optional access for user running the app locally:
* `Azure Service Bus Data Sender`
* `Azure Service Bus Data Receiver`
* `IoT Hub Data Contributor`
* `IoT Hub Registry Contributor`

#### Run the console scheduler

Once the infrastructure is deployed, `appsettings.local.json` files will be created for all projects.

To schedule a job, run `dotnet run` from `console-scheduler` directory.

There are two other test project that just verify azure web pub sub is working:
- `console-cloud` - sample demonstrating the use of IoT hub cloud SDK.
- `console-device` - sample simulating an IoT device.

## Other Considerations

Sample could be extended to use other features of Iot Hub, including:

* [Jobs](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-devguide-jobs)
* [Cloud to device commands](https://learn.microsoft.com/en-us/rest/api/iotcentral/dataplane/devices/run-command?view=rest-iotcentral-dataplane-2022-07-31&tabs=HTTP)
* [Messaging Considertions](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-devguide-messaging)