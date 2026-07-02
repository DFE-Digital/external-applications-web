export type ServiceName = 'transfers' | 'lsrp' | 'visits';

export interface ServiceConfig {
  name: ServiceName;
  url: string;
  username: string;
  apiKey: string;
  tenantId: string;
}
