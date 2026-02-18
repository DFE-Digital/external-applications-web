# Changelog

All notable changes to this service will be documented in this file.

## [1.0.0] – Public Beta
### Notes
- First formally versioned public beta release.

## [1.0.1] – Public Beta
### Notes
- Added Client Side AppInsights SDK.

## [1.1.0] – Public Beta
### Notes
- Added Support for Multi-Tenancy. This service is now a Tenant of EAT API.

## [1.2.0] – Public Beta
### Notes
- As part of the Multi-Tenancy, we have now converted EAT Web to a single repository deployed to multiple services
- Each service will have it's own set of appsettings.json files, and it is decided at the time of the deployment which one is deployed to the container.

## [1.2.1] – Public Beta
### Notes
- Updated LSRP Test env appsettings with an update DSI auth details and Front-Door URL.

## [1.2.2] – Public Beta
### Notes
- Improved Event-Mapping to support multi handlers when an application is submitted.