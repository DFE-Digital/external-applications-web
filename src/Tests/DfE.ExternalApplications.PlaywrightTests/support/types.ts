export type ServiceName = 'Transfers' | 'Lsrp' | 'RGVisits';

export interface ServiceConfig {
  name: ServiceName;
  url: string;
  username: string;
  apiKey: string;
  tenantId: string;
}
