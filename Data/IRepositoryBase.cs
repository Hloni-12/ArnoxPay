using System.Linq.Expressions;

namespace ArnoxPay.Data;

public interface IRepositoryBase<T> where T : class
{
    IQueryable<T> FindAll();
    IQueryable<T> FindByCondition(Expression<Func<T, bool>> expression);
    T? GetById(object id);
    void Create(T entity);
    void Update(T entity);
    void Delete(T entity);
}
