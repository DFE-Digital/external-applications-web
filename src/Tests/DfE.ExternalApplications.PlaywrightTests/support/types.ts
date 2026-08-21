export type ServiceName = 'Transfers' | 'Lsrp' | 'RGVisits';

export interface Terminology {
  singular: string;
  plural: string;
}

export interface ServiceConfig {
  name: ServiceName;
  url: string;
  username: string;
  apiKey: string;
  tenantId: string;
  terminology: Terminology;
}
