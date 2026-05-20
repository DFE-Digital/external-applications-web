module "azurerm_key_vault" {
  source = "github.com/DFE-Digital/terraform-azurerm-key-vault-tfvars?ref=v0.5.3"

  environment                             = local.environment
  project_name                            = local.project_name
  existing_resource_group                 = module.azure_container_apps_hosting.azurerm_resource_group_default.name
  azure_location                          = local.azure_location
  key_vault_access_use_rbac_authorization = true
  key_vault_access_users                  = []
  key_vault_access_ipv4                   = local.key_vault_access_ipv4
  enable_private_endpoint                 = local.enable_keyvault_private_endpoint
  virtual_network_id                      = local.enable_keyvault_private_endpoint ? module.azure_container_apps_hosting.azurerm_virtual_network.id : ""
  virtual_network_name                    = local.enable_keyvault_private_endpoint ? module.azure_container_apps_hosting.azurerm_virtual_network.name : ""
  key_vault_subnet_cidr                   = local.enable_keyvault_private_endpoint ? local.key_vault_subnet_cidr : ""
  tfvars_filename                         = local.tfvars_filename
  diagnostic_log_analytics_workspace_id   = module.azure_container_apps_hosting.azurerm_log_analytics_workspace_container_app.id
  diagnostic_eventhub_name                = ""
  tags                                    = local.tags
}
