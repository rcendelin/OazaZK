using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class HouseRepository : TableStorageRepository<House>, IHouseRepository
{
    public HouseRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.Houses)
    {
    }

    protected override TableEntity ToTableEntity(House entity) =>
        TableEntityMapper.ToTableEntity(entity);

    protected override House FromTableEntity(TableEntity tableEntity) =>
        TableEntityMapper.ToHouse(tableEntity);
}
