module "azure_container_apps_hosting" {
  source = "github.com/DFE-Digital/terraform-azurerm-container-apps-hosting?ref=v2.6.3"

  environment    = local.environment
  project_name   = local.project_name
  azure_location = local.azure_location
  tags           = local.tags

  container_app_environment_internal_load_balancer_enabled = local.container_app_environment_internal_load_balancer_enabled
  existing_container_app_environment                       = local.existing_container_app_environment
  container_app_name_override                              = local.container_app_name_override
  container_app_identities                                 = local.container_app_identities
  existing_virtual_network                                 = local.existing_virtual_network
  container_apps_infra_subnet_cidr                         = local.container_apps_infra_subnet_cidr
  existing_resource_group                                  = local.existing_resource_group
  container_apps_allow_ips_inbound                         = local.container_apps_allow_ips_inbound
  restrict_container_apps_to_cdn_inbound_only              = false

  enable_container_registry             = local.enable_container_registry
  registry_admin_enabled                = local.registry_admin_enabled
  registry_use_managed_identity         = local.registry_use_managed_identity
  registry_managed_identity_assign_role = local.registry_managed_identity_assign_role
  registry_server                       = local.registry_server

  enable_dns_zone      = local.enable_dns_zone
  dns_zone_domain_name = local.dns_zone_domain_name
  dns_ns_records       = local.dns_ns_records
  dns_txt_records      = local.dns_txt_records
  dns_mx_records       = local.dns_mx_records

  image_name                             = local.image_name
  container_command                      = local.container_command
  container_secret_environment_variables = local.container_secret_environment_variables
  container_scale_http_concurrency       = local.container_scale_http_concurrency
  container_min_replicas                 = local.container_min_replicas
  container_port                         = local.container_port
  enable_health_insights_api             = local.enable_health_insights_api
  health_insights_api_cors_origins       = local.health_insights_api_cors_origins
  health_insights_api_ipv4_allow_list    = local.health_insights_api_ipv4_allow_list

  enable_monitoring            = local.enable_monitoring
  monitor_email_receivers      = local.monitor_email_receivers
  container_health_probe_path  = local.container_health_probe_path
  monitor_endpoint_healthcheck = local.monitor_endpoint_healthcheck

  existing_logic_app_workflow                  = local.existing_logic_app_workflow
  existing_network_watcher_name                = local.existing_network_watcher_name
  existing_network_watcher_resource_group_name = local.existing_network_watcher_resource_group_name

  monitor_http_availability_fqdn = local.monitor_http_availability_fqdn
  dns_alias_records              = local.dns_alias_records

  enable_monitoring_traces = local.enable_monitoring_traces
}
