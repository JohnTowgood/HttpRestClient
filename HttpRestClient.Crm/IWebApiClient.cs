using HttpRestClient.Crm.Models;
using System;
using System.Threading.Tasks;
using Action = HttpRestClient.Crm.Models.Action;

namespace HttpRestClient.Crm
{
    public interface IWebApiClient
    {
        void SetupEntitySetNames();
        Task<EntityCollection> RetrieveMultipleAsync(string query);
        Task<Entity> RetrieveAsync(string entityName, Guid id, ColumnSet columnSet);
        Task<Entity> RetrieveAsync(string entityName, AlternateKeys keys, ColumnSet columnSet);
        Guid Create(Entity entity);
        Guid Upsert(Entity entity);
        Entity Upsert(Entity entity, ColumnSet columnSet);
        void Update(Entity entity);
        Entity Update(Entity entity, ColumnSet columnSet);
        void Execute(Action action);
        void Execute(Function function);
        void Associate(EntityReference from, string relationship, EntityReference to);
        void Disassociate(EntityReference from, string relationship, EntityReference to);
    }
}
