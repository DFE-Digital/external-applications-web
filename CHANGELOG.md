# Changelog

All notable changes to this service will be documented in this file.

## [1.0.0]
### Notes
- First formally versioned public beta release.

## [1.0.1]
### Notes
- Added Client Side AppInsights SDK.

## [1.1.0]
### Notes
- Added Support for Multi-Tenancy. This service is now a Tenant of EAT API.

## [1.2.0]
### Notes
- As part of the Multi-Tenancy, we have now converted EAT Web to a single repository deployed to multiple services
- Each service will have it's own set of appsettings.json files, and it is decided at the time of the deployment which one is deployed to the container.

## [1.2.1]
### Notes
- Updated LSRP Test env appsettings with an update DSI auth details and Front-Door URL.

## [1.2.2]
### Notes
- Improved Event-Mapping to support multi handlers when an application is submitted.

## [1.2.3]
### Notes
- Added postcode to the Academies Auto-Complete confirmation page.
- Fixed a bug in Auto-Complete where duplicate items couldn't be selected.

## [1.2.4]
### Notes
- Enabled Test Auth in LSRP Test Environment.

## [1.2.5]
### Notes
- Updated Collection Flows to save user changes on each click of Save and Continue

## [1.2.6]
### Notes
- Fixed Collection Flow validation and conditional logic issues